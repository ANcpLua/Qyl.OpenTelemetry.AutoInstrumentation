using System.Text.Json;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Qyl.Build.ApiBaseline;

/// <summary>
/// The public API of every shipped project, as a report that never fails the gate. It reads the
/// surface of each Release assembly, rewrites <c>src/&lt;project&gt;/PublicAPI.txt</c> to it, so
/// <c>git diff</c> shows an API change next to the code that caused it, and counts the lines added and
/// removed against the file in HEAD. The count becomes the target's summary note ("+N -M changed" or
/// "unchanged"), each line goes to the log, and a release reads the diff to choose its version bump.
/// </summary>
static class ApiBaselineReport
{
    /// <summary>The line format of Roslyn's PublicApiAnalyzers: one line per type and member,
    /// property accessors instead of properties, nullable annotations included.</summary>
    private static readonly SymbolDisplayFormat Format = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.OmittedAsContaining,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
        propertyStyle: SymbolDisplayPropertyStyle.ShowReadWriteDescriptor,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeParameters | SymbolDisplayMemberOptions.IncludeContainingType
            | SymbolDisplayMemberOptions.IncludeExplicitInterface | SymbolDisplayMemberOptions.IncludeModifiers
            | SymbolDisplayMemberOptions.IncludeConstantValue,
        parameterOptions: SymbolDisplayParameterOptions.IncludeExtensionThis | SymbolDisplayParameterOptions.IncludeParamsRefOut
            | SymbolDisplayParameterOptions.IncludeType | SymbolDisplayParameterOptions.IncludeName
            | SymbolDisplayParameterOptions.IncludeDefaultValue,
        miscellaneousOptions: SymbolDisplayMiscellaneousOptions.UseSpecialTypes
            | SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
            | SymbolDisplayMiscellaneousOptions.IncludeNotNullableReferenceTypeModifier);

    public static string Run()
    {
        List<(AbsolutePath Baseline, string[] Surface, string[] Committed)> projects;
        try
        {
            projects = Repo.PackableProjects
                .Select(static project => project.Parent / "PublicAPI.txt")
                .Select(static baseline => (baseline, Surface(baseline.Parent), Committed(baseline)))
                .ToList();
        }
        catch (Exception exception) // a report: an unreadable surface is a note, never a failed gate
        {
            Log.Warning("public API not computed: {Message}", exception.Message);
            return "not computed, see log";
        }

        int added = 0, removed = 0;
        foreach (var (baseline, surface, committed) in projects)
        {
            foreach (var line in committed.Except(surface, StringComparer.Ordinal))
            {
                Log.Information("api {File} - {Line}", Repo.Relative(baseline), line);
                removed++;
            }

            foreach (var line in surface.Except(committed, StringComparer.Ordinal))
            {
                Log.Information("api {File} + {Line}", Repo.Relative(baseline), line);
                added++;
            }

            var text = string.Join('\n', surface) + "\n";
            if (!baseline.FileExists() || File.ReadAllText(baseline) != text)
                File.WriteAllText(baseline, text);
        }

        return added + removed == 0 ? "unchanged" : $"+{added} -{removed} changed";
    }

    private static string[] Committed(AbsolutePath baseline)
    {
        var result = Shell.Run("git", ["show", "HEAD:" + Repo.Relative(baseline)], Repo.Root);
        return result.ExitCode == 0 ? result.Stdout.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) : [];
    }

    /// <summary>The public surface of the project's Release assembly, sorted by name so a type's
    /// members stay together whatever their modifiers.</summary>
    private static string[] Surface(AbsolutePath projectDirectory)
    {
        var name = projectDirectory.Name;
        var assembly = MetadataReference.CreateFromFile(Repo.BinAssembly(projectDirectory / $"{name}.csproj"));
        var compilation = CSharpCompilation.Create("public-api", references: [assembly, .. References(name).Select(static path => MetadataReference.CreateFromFile(path))]);
        var symbol = (IAssemblySymbol)compilation.GetAssemblyOrModuleSymbol(assembly)!;

        var resolved = compilation.References.Select(compilation.GetAssemblyOrModuleSymbol).OfType<IAssemblySymbol>().Select(static reference => reference.Identity.Name);
        var unresolved = symbol.Modules.Single().ReferencedAssemblies.Select(static identity => identity.Name).Except(resolved, StringComparer.Ordinal).ToArray();
        Gate.Require(unresolved.Length == 0, () => $"{name}: unresolved references {string.Join(", ", unresolved)}");

        var lines = new List<string>();
        Collect(symbol.GlobalNamespace, lines);
        return lines.Distinct(StringComparer.Ordinal).OrderBy(SortKey, StringComparer.Ordinal).ThenBy(static line => line, StringComparer.Ordinal).ToArray();
    }

    private static void Collect(INamespaceOrTypeSymbol container, List<string> lines)
    {
        foreach (var member in container.GetMembers())
        {
            if (member is INamespaceSymbol nested)
            {
                Collect(nested, lines);
                continue;
            }

            // Roslyn gives every value type read from metadata a parameterless constructor; an enum's is
            // no API.
            if (member.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Protected or Accessibility.ProtectedOrInternal)
                || member is IPropertySymbol
                    or IMethodSymbol { MethodKind: MethodKind.EventAdd or MethodKind.EventRemove }
                    or IMethodSymbol { MethodKind: MethodKind.Constructor, ContainingType.TypeKind: TypeKind.Enum })
                continue;

            var type = member switch { IMethodSymbol method => method.ReturnType, IFieldSymbol field => field.Type, IEventSymbol @event => @event.Type, _ => null };
            lines.Add(type is null ? member.ToDisplayString(Format) : $"{member.ToDisplayString(Format)} -> {type.ToDisplayString(Format)}");
            if (member is INamedTypeSymbol namedType)
                Collect(namedType, lines);
        }
    }

    /// <summary>The line without its leading modifiers (static, const, abstract, ...).</summary>
    private static string SortKey(string line)
    {
        var words = line.Split(' ');
        var first = Array.FindIndex(words, static word => !word.All(char.IsAsciiLetterLower));
        return string.Join(' ', words[Math.Max(first, 0)..]);
    }

    /// <summary>What the compiler referenced, from the project's project.assets.json: the compile assets
    /// of every package, the Release assemblies of the referenced projects and the targeting packs of
    /// the framework references.</summary>
    private static List<string> References(string project)
    {
        using var assets = JsonDocument.Parse(File.ReadAllText(Repo.Artifacts / "obj" / project / "project.assets.json"));
        var root = assets.RootElement;
        var folders = root.GetProperty("packageFolders").EnumerateObject().Select(static folder => folder.Name).ToArray();
        var target = root.GetProperty("targets").EnumerateObject().Single();
        var references = new List<string>();
        foreach (var library in target.Value.EnumerateObject())
        {
            if (library.Value.GetProperty("type").GetString() == "project")
            {
                references.Add(Repo.BinAssembly(Repo.SourceProject(library.Name.Split('/')[0])));
                continue;
            }

            if (!library.Value.TryGetProperty("compile", out var compile))
                continue;
            var path = root.GetProperty("libraries").GetProperty(library.Name).GetProperty("path").GetString()!;
            foreach (var asset in compile.EnumerateObject().Select(static asset => asset.Name).Where(static asset => !asset.EndsWith("/_._", StringComparison.Ordinal)))
                references.Add(folders.Select(folder => Path.Combine(folder, path, asset)).First(File.Exists));
        }

        // The SDK this build runs on holds the targeting packs; the runtime's own directory is
        // <dotnet>/shared/Microsoft.NETCore.App/<version>/.
        var dotnet = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(typeof(object).Assembly.Location)!, "..", "..", ".."));
        var frameworks = root.GetProperty("project").GetProperty("frameworks").GetProperty(target.Name).TryGetProperty("frameworkReferences", out var listed)
            ? listed.EnumerateObject().Select(static framework => framework.Name).ToArray()
            : [];
        foreach (var framework in frameworks.Append("Microsoft.NETCore.App").Distinct(StringComparer.Ordinal))
        {
            var pack = Directory.GetDirectories(Path.Combine(dotnet, "packs", framework + ".Ref"))
                .Where(version => Directory.Exists(Path.Combine(version, "ref", target.Name)))
                .MaxBy(static version => Version.Parse(Path.GetFileName(version).Split('-')[0]));
            Gate.Require(pack is not null, $"no {framework}.Ref targeting pack for {target.Name} under {dotnet}");
            references.AddRange(Directory.GetFiles(Path.Combine(pack!, "ref", target.Name), "*.dll"));
        }

        return references;
    }
}
