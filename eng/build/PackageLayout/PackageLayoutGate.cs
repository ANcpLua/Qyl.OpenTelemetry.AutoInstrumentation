using System.IO.Compression;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Xml.Linq;

namespace Qyl.Build;

/// <summary>The packed Qyl.Telemetry.AutoInstrumentation: required build assets present, identical in
/// build/ and buildTransitive/, no entry or token of a forbidden instrumentation mechanism, and the
/// semantic-convention release it compiled against readable by a consumer as the pin.</summary>
static class PackageLayoutGate
{
    private static readonly string[] RequiredPackageEntries =
    [
        "analyzers/dotnet/cs/Qyl.Telemetry.AutoInstrumentation.SourceGenerators.dll",
        "build/Qyl.Telemetry.AutoInstrumentation.targets",
        "buildTransitive/Qyl.Telemetry.AutoInstrumentation.targets",
    ];

    private static readonly string[] ForbiddenPackageEntryTokens = ["profiler", "startuphook", "startup-hook", "ilrewrite", "il-rewrite", "native/", "runtimes/"];

    private static readonly string[] ForbiddenContentTokens =
    [
        "CORECLR_PROFILER", "DOTNET_STARTUP_HOOKS", "ICorProfiler", "ReJIT", "ILRewrite", "ILRewriter", "Assembly.Load",
        "System.Reflection", "Activator.CreateInstance", "dynamic ",
    ];

    public static void Verify()
    {
        var version = LocalFeed.EnsurePacked();
        var package = LocalFeed.Directory / $"Qyl.Telemetry.AutoInstrumentation.{version}.nupkg";
        Gate.Require(package.FileExists(), $"package missing: {package}");
        VerifyPackage(package);
        Log.Information("package-layout-ok");
    }

    private static void VerifyPackage(AbsolutePath package)
    {
        using var archive = ZipFile.OpenRead(package);
        var names = archive.Entries.Select(static entry => entry.FullName).ToHashSet(StringComparer.Ordinal);
        var missing = RequiredPackageEntries.Where(entry => !names.Contains(entry)).ToArray();
        Gate.Require(missing.Length == 0, () => $"package missing required entries: {ContractValue.RenderSorted(missing)}");

        foreach (var name in names.Sorted())
        {
            var lowered = name.ToLowerInvariant();
            foreach (var token in ForbiddenPackageEntryTokens)
                Gate.Require(!lowered.Contains(token, StringComparison.Ordinal), $"package contains forbidden mechanism entry token {token}: {name}");
            Gate.Require(!name.EndsWith("InterceptsLocationAttribute.g.cs", StringComparison.Ordinal),
                $"package must not ship a compilation-wide InterceptsLocationAttribute source: {name}");
        }

        var buildTargets = Read(archive, "build/Qyl.Telemetry.AutoInstrumentation.targets");
        var transitiveTargets = Read(archive, "buildTransitive/Qyl.Telemetry.AutoInstrumentation.targets");
        var nuspecName = names.FirstOrDefault(static name => name.EndsWith(".nuspec", StringComparison.Ordinal)) ?? throw Gate.Fail("package nuspec missing");
        var nuspec = Read(archive, nuspecName);
        Gate.Require(buildTargets == transitiveTargets, "build and buildTransitive targets diverged");
        VerifySemanticConventionsVersion(archive, nuspec);

        VerifyTargets("build targets", buildTargets);
        VerifyTargets("buildTransitive targets", transitiveTargets);
        foreach (var (name, text) in new[] { ("build targets", buildTargets), ("buildTransitive targets", transitiveTargets), ("nuspec", nuspec) })
        {
            foreach (var token in ForbiddenContentTokens)
                Gate.Require(!text.Contains(token, StringComparison.Ordinal), $"{name} contains forbidden mechanism token: {token}");
        }
    }

    /// <summary>A consumer reads the compiled-against Qyl.Telemetry.SemanticConventions release from the
    /// nuspec dependency's lower bound and from the assembly's QylSemanticConventionsVersion metadata;
    /// both must be the Directory.Packages.props pin.</summary>
    private static void VerifySemanticConventionsVersion(ZipArchive archive, string nuspec)
    {
        var pin = Repo.Pin(Repo.SemanticConventionsPackage);
        var dependencies = XDocument.Parse(nuspec).Descendants()
            .Where(static element => element.Name.LocalName == "dependency" && (string?)element.Attribute("id") == Repo.SemanticConventionsPackage)
            .Select(static element => (string?)element.Attribute("version") ?? "")
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        Gate.Require(dependencies.SequenceEqual([pin]),
            () => $"nuspec depends on {Repo.SemanticConventionsPackage} {ContractValue.RenderSorted(dependencies)}, expected exactly {pin}");

        var assemblyName = $"lib/{Repo.TargetFramework}/Qyl.Telemetry.AutoInstrumentation.dll";
        var entry = archive.GetEntry(assemblyName) ?? throw Gate.Fail($"package entry missing: {assemblyName}");
        using var image = new MemoryStream();
        using (var stream = entry.Open())
            stream.CopyTo(image);
        image.Position = 0;
        using var pe = new PEReader(image);
        var reader = pe.GetMetadataReader();
        var stamped = reader.GetAssemblyDefinition().GetCustomAttributes()
            .Select(handle => AssemblyMetadata(reader, reader.GetCustomAttribute(handle)))
            .Where(static pair => pair?.Key == "QylSemanticConventionsVersion")
            .Select(static pair => pair!.Value.Value)
            .ToArray();
        Gate.Require(stamped.SequenceEqual([pin]),
            () => $"{assemblyName} carries QylSemanticConventionsVersion {ContractValue.RenderSorted(stamped)}, expected exactly {pin}");
        Log.Information("semantic-conventions-version-ok {Version} (nuspec dependency and assembly metadata)", pin);
    }

    /// <summary>Decodes an [assembly: AssemblyMetadata(key, value)] without loading the assembly.</summary>
    private static (string Key, string Value)? AssemblyMetadata(MetadataReader reader, CustomAttribute attribute)
    {
        if (attribute.Constructor.Kind != HandleKind.MemberReference)
            return null;
        var parent = reader.GetMemberReference((MemberReferenceHandle)attribute.Constructor).Parent;
        if (parent.Kind != HandleKind.TypeReference)
            return null;
        var type = reader.GetTypeReference((TypeReferenceHandle)parent);
        if (reader.GetString(type.Namespace) != "System.Reflection" || reader.GetString(type.Name) != "AssemblyMetadataAttribute")
            return null;
        var blob = reader.GetBlobReader(attribute.Value);
        if (blob.ReadUInt16() != 1)
            return null;
        return (blob.ReadSerializedString() ?? "", blob.ReadSerializedString() ?? "");
    }

    private static string Read(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name) ?? throw Gate.Fail($"package entry missing: {name}");
        using var reader = new StreamReader(entry.Open());
        return reader.ReadToEnd();
    }

    private static void VerifyTargets(string name, string text)
    {
        foreach (var token in new[]
                 {
                     "QylAutoInstrumentationCoreBuildAssetsImported",
                     "_QylAutoInstrumentationCoreBuildAssetsAlreadyImported",
                     "<InterceptorsNamespaces>$(InterceptorsNamespaces);Qyl.Telemetry.AutoInstrumentation.Generated</InterceptorsNamespaces>",
                 })
        {
            Gate.Require(text.Contains(token, StringComparison.Ordinal), $"{name} missing token: {token}");
        }

        foreach (var token in new[] { "InterceptorsPreviewNamespaces", "Qyl.Telemetry.AutoInstrumentation.InterceptsLocationAttribute.g.cs", "QylAutoInstrumentationDisableInterceptorsAttribute" })
            Gate.Require(!text.Contains(token, StringComparison.Ordinal), $"{name} contains obsolete package target token: {token}");
    }
}
