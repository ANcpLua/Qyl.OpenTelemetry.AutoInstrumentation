using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;
using Item = System.Collections.Generic.OrderedDictionary<string, object?>;

namespace Qyl.Build;

/// <summary>
/// The interceptor manifests the source generator writes beside every intercepted call
/// (<c>// qyl-interceptor-manifest: {...}</c>): their canonical shape, the verified JSONL artifact, and
/// the live collection from every demo that evidences an implemented signal.
/// </summary>
static partial class GeneratorManifests
{
    public const string ManifestPrefix = "// qyl-interceptor-manifest: ";
    public const string GeneratedInterceptorFile = "QylAutoInstrumentation.Interceptors.g.cs";
    public const string GeneratorOutputSegment = "Qyl.Telemetry.AutoInstrumentation.SourceGenerators";

    public static readonly string[] ManifestFields = ["interceptorKind", "signal", "instrumentationId", "additionalMetricIds", "contractKeys"];

    public static Item StrictJsonObject(string payload, string context)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(payload);
        }
        catch (JsonException error)
        {
            throw Gate.Fail($"invalid interceptor manifest JSON in {context}: {error.Message}");
        }

        using (document)
        {
            if (document.RootElement.ValueKind is not JsonValueKind.Object)
                throw Gate.Fail($"interceptor manifest must be a JSON object in {context}");
            return (Item)Convert(document.RootElement, context)!;
        }
    }

    private static object? Convert(JsonElement element, string context) => element.ValueKind switch
    {
        JsonValueKind.Object => ConvertObject(element, context),
        JsonValueKind.Array => element.EnumerateArray().Select(child => Convert(child, context)).ToList(),
        JsonValueKind.String => element.GetString(),
        JsonValueKind.Number => element.TryGetInt64(out var number) ? number : element.GetDouble(),
        JsonValueKind.True => true,
        JsonValueKind.False => false,
        _ => null,
    };

    private static Item ConvertObject(JsonElement element, string context)
    {
        var result = new Item();
        foreach (var property in element.EnumerateObject())
        {
            if (result.ContainsKey(property.Name))
                throw Gate.Fail($"invalid interceptor manifest JSON in {context}: duplicate JSON key {ContractValue.Render(property.Name)}");
            result[property.Name] = Convert(property.Value, context);
        }

        return result;
    }

    public static void VerifyManifestShape(Item manifest, string context)
    {
        Gate.Require(manifest.Keys.SequenceEqual(ManifestFields),
            () => $"interceptor manifest fields are not canonical in {context}: expected=({string.Join(", ", ManifestFields)}) actual=({string.Join(", ", manifest.Keys)})");

        var kind = manifest["interceptorKind"];
        var signal = manifest["signal"];
        var instrumentationId = manifest["instrumentationId"];
        var additionalMetricIds = manifest["additionalMetricIds"];
        var contractKeys = manifest["contractKeys"];
        Gate.Require(kind is string { Length: > 0 }, $"interceptor manifest kind must be a non-empty string in {context}");
        Gate.Require(signal is "traces" or "metrics" or "logs", $"interceptor manifest signal is invalid in {context}: {ContractValue.Render(signal)}");
        Gate.Require(instrumentationId is string id && IdPattern().IsMatch(id), $"interceptor manifest instrumentation id is invalid in {context}: {ContractValue.Render(instrumentationId)}");
        Gate.Require(additionalMetricIds is List<object?> metricIds && metricIds.All(static item => item is string text && IdPattern().IsMatch(text)),
            $"interceptor manifest additional metric ids are invalid in {context}");
        var metrics = ((List<object?>)additionalMetricIds!).Cast<string>().ToArray();
        Gate.Require(metrics.Distinct(StringComparer.Ordinal).Count() == metrics.Length, $"interceptor manifest additional metric ids contain duplicates in {context}");
        Gate.Require(contractKeys is List<object?> keys && keys.All(static item => item is string), $"interceptor manifest contract keys are invalid in {context}");
        var actualKeys = ((List<object?>)contractKeys!).Cast<string>().ToArray();

        string[] expectedKeys = [$"signals.{signal}.{instrumentationId}", .. metrics.Select(static item => $"signals.metrics.{item}")];
        Gate.Require(actualKeys.SequenceEqual(expectedKeys),
            () => $"interceptor manifest contract keys are not canonically derived in {context}: expected={ContractValue.Render(expectedKeys.ToList())} actual={ContractValue.Render(actualKeys.ToList())}");
        Gate.Require(actualKeys.Distinct(StringComparer.Ordinal).Count() == actualKeys.Length, $"interceptor manifest contract keys contain duplicates in {context}");
    }

    /// <summary>Compact JSON in canonical field order, non-ASCII kept, as the generator writes it.</summary>
    public static string CanonicalManifestLine(Item manifest)
    {
        VerifyManifestShape(manifest, "canonical manifest");
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            writer.WriteStartObject();
            foreach (var field in ManifestFields)
            {
                writer.WritePropertyName(field);
                switch (manifest[field])
                {
                    case string text:
                        writer.WriteStringValue(text);
                        break;
                    case List<object?> list:
                        writer.WriteStartArray();
                        foreach (var entry in list.Cast<string>())
                            writer.WriteStringValue(entry);
                        writer.WriteEndArray();
                        break;
                }
            }

            writer.WriteEndObject();
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    public static List<Item> ParseGeneratedInterceptorSource(string text, string source)
    {
        var lines = text.Split('\n').Select(static line => line.TrimEnd('\r')).ToArray();
        var manifestLines = lines
            .Select((line, index) => (Index: index, Line: line.Trim()))
            .Where(static entry => entry.Line.StartsWith(ManifestPrefix, StringComparison.Ordinal))
            .Select(static entry => (entry.Index, Payload: entry.Line[ManifestPrefix.Length..]))
            .ToArray();
        var interceptorLines = lines
            .Select((line, index) => (Index: index, Line: line.Trim()))
            .Where(static entry => entry.Line.StartsWith("// Intercepted call at ", StringComparison.Ordinal))
            .Select(static entry => entry.Index)
            .ToArray();
        Gate.Require(interceptorLines.Length > 0, $"generated interceptor source contains no intercepted calls: {source}");
        Gate.Require(manifestLines.Select(static entry => entry.Index).SequenceEqual(interceptorLines.Select(static index => index - 1)),
            $"every generated interceptor must have exactly one adjacent manifest: {source}");

        var manifests = new List<Item>();
        foreach (var (index, payload) in manifestLines)
        {
            var context = $"{source}:{index + 1}";
            var manifest = StrictJsonObject(payload, context);
            VerifyManifestShape(manifest, context);
            manifests.Add(manifest);
        }

        return manifests;
    }

    public static List<Item> ParseVerifiedManifestArtifact(string text, string source)
    {
        Gate.Require(text.EndsWith('\n'), $"verified interceptor manifest artifact must end with a newline: {source}");
        var lines = text[..^1].Split('\n');
        Gate.Require(lines.Length > 0 && lines.All(static line => line.Length > 0),
            $"verified interceptor manifest artifact must contain only non-empty JSON lines: {source}");

        var manifests = new List<Item>();
        var canonical = new List<string>();
        for (var index = 0; index < lines.Length; index++)
        {
            var context = $"{source}:{index + 1}";
            var manifest = StrictJsonObject(lines[index], context);
            VerifyManifestShape(manifest, context);
            manifests.Add(manifest);
            canonical.Add(CanonicalManifestLine(manifest));
        }

        Gate.Require(lines.SequenceEqual(canonical.Distinct(StringComparer.Ordinal).Sorted()),
            $"verified interceptor manifest artifact is not unique and ordinal-sorted: {source}");
        return manifests;
    }

    public static string RenderManifestArtifact(IEnumerable<Item> manifests)
    {
        var lines = manifests.Select(CanonicalManifestLine).Distinct(StringComparer.Ordinal).Sorted().ToArray();
        Gate.Require(lines.Length > 0, "live generator builds emitted no interceptor manifests");
        return string.Join("\n", lines) + "\n";
    }

    /// <summary>The single demo project behind every implemented signal's demos/ evidence.</summary>
    public static IReadOnlyList<AbsolutePath> ImplementedSignalDemoProjects()
    {
        var contract = ContractModel.Load();
        var projects = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in ContractModel.ImplementedSignalItems(contract))
        {
            var evidenceDirectories = ContractModel.Evidence(item)
                .Where(static entry => entry.StartsWith("demos/", StringComparison.Ordinal))
                .Select(static entry => Repo.Root / entry)
                .ToArray();
            Gate.Require(evidenceDirectories.Length > 0, $"implemented signal item has no demo evidence: {item.GetText("key")}");
            foreach (var directory in evidenceDirectories)
            {
                var candidates = directory.DirectoryExists() ? directory.GlobFiles("*.csproj").OrderBy(static path => path.ToString(), StringComparer.Ordinal).ToArray() : [];
                Gate.Require(candidates.Length == 1,
                    $"implemented signal demo evidence must contain exactly one project for {item.GetText("key")}: {Repo.Relative(directory)}");
                projects.Add(candidates[0]);
            }
        }

        return projects.Sorted().Select(static path => (AbsolutePath)path).ToArray();
    }

    /// <summary>Builds every implemented-signal demo with its generated sources written out and collects
    /// the canonical manifest artifact from them.</summary>
    public static string CollectLiveManifestArtifact()
    {
        var projects = ImplementedSignalDemoProjects();
        Gate.Require(projects.Count > 0, "the implemented signal contract selected no demo projects");

        var manifests = new List<Item>();
        var root = Repo.Artifacts / "generator-manifests";
        root.CreateOrCleanDirectory();
        var artifactsRoot = root / "artifacts";
        try
        {
            foreach (var project in projects)
            {
                var generatedRoot = root / "generated" / project.NameWithoutExtension;
                var result = Shell.Run("dotnet",
                [
                    "build", project, "-c", "Release", "--disable-build-servers", "--artifacts-path", artifactsRoot,
                    "-maxcpucount:4", "-nodeReuse:false", "-v", "quiet", "-nologo",
                    "-p:NuGetAudit=false", "-p:RestoreIgnoreFailedSources=true", "-p:UseSharedCompilation=false",
                    "-p:WarningsNotAsErrors=NU1801", "-p:EmitCompilerGeneratedFiles=true", $"-p:CompilerGeneratedFilesOutputPath={generatedRoot}",
                ], Repo.Root);
                Gate.Require(result.ExitCode == 0, () => $"generator manifest demo build failed: {Repo.Relative(project)}\n{result.Describe()}");

                var generatedFiles = generatedRoot.DirectoryExists()
                    ? generatedRoot.GlobFiles("**/" + GeneratedInterceptorFile)
                        .Where(static path => path.ToString().Split(Path.DirectorySeparatorChar).Contains(GeneratorOutputSegment, StringComparer.Ordinal))
                        .OrderBy(static path => path.ToString(), StringComparer.Ordinal)
                        .ToArray()
                    : [];
                Gate.Require(generatedFiles.Length <= 1,
                    $"expected at most one generated interceptor source for {Repo.Relative(project)}, found {generatedFiles.Length}");
                if (generatedFiles.Length == 0)
                    continue;
                manifests.AddRange(ParseGeneratedInterceptorSource(File.ReadAllText(generatedFiles[0]), generatedFiles[0]));
            }
        }
        finally
        {
            root.DeleteDirectory();
        }

        return RenderManifestArtifact(manifests);
    }

    [GeneratedRegex("^[A-Z0-9]+$")]
    private static partial Regex IdPattern();
}
