using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Qyl.Contract.Generator;

/// <summary>
/// Experiment: qyl's contract as compilation-native Roslyn symbols.
///
///   PHASE A (pre-compilation, AdditionalTexts ONLY): parse the resolved-contract TSV and the
///     semantic-seed TSV; emit the contract as strongly-typed symbols into the INITIAL compilation
///     (namespace Qyl.Generated.Contract): per-item marker types, InstrumentationCapability records,
///     SemanticAttributeDescriptor records, and a SemanticSeeds constant table. This replaces the
///     hand-authored InstrumentationContract.cs string/dictionary tables.
///
///   PHASE B (standard, reads the augmented compilation + user C#): BIND the pre-compilation symbols
///     via GetTypeByMetadataName (the compilation is the cache — no YAML re-parse), read the seed map
///     back out of the bound SemanticSeeds symbol's constants, then inspect USER DTO properties and
///     emit deterministic semantic-convention bindings (CustomerId -> customer.id). That inference is
///     compile-time-only: the runtime DiagnosticListeners never see user DTO shapes.
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class ContractPreCompilationGenerator : IIncrementalGenerator
{
    private const string Ns = "Qyl.Generated.Contract";

    private static readonly DiagnosticDescriptor PreCompMissing = new(
        "QYLC001", "Pre-compilation contract symbol missing",
        "GetTypeByMetadataName('{0}') returned null — the pre-compilation contract did NOT land in the compilation",
        "QylContract", DiagnosticSeverity.Error, isEnabledByDefault: true);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        // ---------------- PHASE A: PRE-COMPILATION (non-compilation inputs only) ----------------
        var contractText = context.AdditionalTextsProvider
            .Where(static t => t.Path.Replace('\\', '/').EndsWith("qyl-contract.tsv", StringComparison.OrdinalIgnoreCase))
            .Select(static (t, ct) => t.GetText(ct)?.ToString() ?? string.Empty)
            .Collect();

        context.RegisterPreCompilationSourceOutput(contractText, static (spc, texts) =>
        {
            var rows = ParseContract(string.Concat(texts));
            spc.AddSource("Contract.Capabilities.g.cs", SourceText.From(BuildContractSource(rows), Encoding.UTF8));
        });

        var seedText = context.AdditionalTextsProvider
            .Where(static t => t.Path.Replace('\\', '/').EndsWith("semantic-seeds.tsv", StringComparison.OrdinalIgnoreCase))
            .Select(static (t, ct) => t.GetText(ct)?.ToString() ?? string.Empty)
            .Collect();

        context.RegisterPreCompilationSourceOutput(seedText, static (spc, texts) =>
        {
            var seeds = ParseSeeds(string.Concat(texts));
            spc.AddSource("Contract.SemanticSeeds.g.cs", SourceText.From(BuildSeedsSource(seeds), Encoding.UTF8));
        });

        // ---------------- PHASE B: STANDARD (compilation + user code) ----------------
        context.RegisterSourceOutput(context.CompilationProvider, static (spc, compilation) =>
        {
            var registry = compilation.GetTypeByMetadataName($"{Ns}.ContractRegistry");
            var seedsType = compilation.GetTypeByMetadataName($"{Ns}.SemanticSeeds");
            if (registry is null) { spc.ReportDiagnostic(Diagnostic.Create(PreCompMissing, Location.None, $"{Ns}.ContractRegistry")); return; }
            if (seedsType is null) { spc.ReportDiagnostic(Diagnostic.Create(PreCompMissing, Location.None, $"{Ns}.SemanticSeeds")); return; }

            // Bound from the pre-compilation symbol — the compilation is the cache, not a re-parsed file.
            var boundCount = registry.GetMembers("CapabilityCount").OfType<IFieldSymbol>()
                .Select(f => f.ConstantValue).OfType<int>().FirstOrDefault();

            // Seed map read out of the bound SemanticSeeds symbol's const fields.
            var seedMap = seedsType.GetMembers().OfType<IFieldSymbol>()
                .Where(f => f.HasConstantValue && f.ConstantValue is string)
                .ToDictionary(f => f.Name, f => (string)f.ConstantValue!, StringComparer.Ordinal);

            // Compile-time-only semantic inference over USER types (deterministic, reproducible).
            var bindings = InferBindings(compilation, seedMap);
            spc.AddSource("Contract.Binding.g.cs", SourceText.From(BuildBindingSource(boundCount, bindings), Encoding.UTF8));
        });
    }

    // ---- parsing (trivial; the YAML source of truth stays Python-owned) ----
    private readonly record struct ContractRow(string Key, string Signal, string Lane, string Status, string Evidence,
        string[] Required, string[] Recommended, string[] OptIn);

    private static List<ContractRow> ParseContract(string tsv)
    {
        var rows = new List<ContractRow>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            if (c.Length < 8) continue;
            rows.Add(new ContractRow(c[0], c[1], c[2], c[3], c[4], Csv(c[5]), Csv(c[6]), Csv(c[7])));
        }
        return rows;
    }

    private static string[] Csv(string s) =>
        s.Length == 0 ? Array.Empty<string>() : s.Split(',').Where(x => x.Length > 0).ToArray();

    private static List<KeyValuePair<string, string>> ParseSeeds(string tsv)
    {
        var seeds = new List<KeyValuePair<string, string>>();
        foreach (var raw in tsv.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            if (line.Length == 0 || line[0] == '#') continue;
            var c = line.Split('\t');
            if (c.Length < 2) continue;
            seeds.Add(new KeyValuePair<string, string>(c[0].Trim(), c[1].Trim()));
        }
        return seeds;
    }

    // ---- emission: the contract as symbols ----
    private static string PascalKey(string key) =>
        string.Concat(key.Split('.', '_').Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p.Substring(1)));

    private static string BuildContractSource(List<ContractRow> rows)
    {
        var b = new StringBuilder();
        b.AppendLine("// <auto-generated/> pre-compilation phase: contract -> compilation-native symbols.");
        b.AppendLine("#nullable enable");
        b.AppendLine($"namespace {Ns};");
        b.AppendLine();
        b.AppendLine("public enum CapabilityStatus { Implemented, ControlBound, OptionBound, ResearchRequired, UnsupportedNativeAot }");
        b.AppendLine("public enum AttributeRequirement { Required, Recommended, OptIn }");
        b.AppendLine("public sealed record InstrumentationCapability(string Key, CapabilityStatus Status, string Lane, string Evidence);");
        b.AppendLine("public sealed record SemanticAttributeDescriptor(string OwningKey, string AttributeName, AttributeRequirement Requirement);");
        b.AppendLine();

        // per-item marker types (one symbol per contract row)
        foreach (var r in rows)
            b.AppendLine($"/// <summary>Marker for contract row <c>{r.Key}</c> ({r.Status}).</summary>\npublic sealed partial class {PascalKey(r.Key)} {{ public const string Key = \"{r.Key}\"; }}");
        b.AppendLine();

        b.AppendLine("public static class ContractRegistry");
        b.AppendLine("{");
        b.AppendLine($"    public const int CapabilityCount = {rows.Count};");
        b.AppendLine("    public static readonly InstrumentationCapability[] Capabilities =");
        b.AppendLine("    {");
        foreach (var r in rows)
            b.AppendLine($"        new(\"{r.Key}\", CapabilityStatus.{Status(r.Status)}, \"{r.Lane}\", \"{r.Evidence}\"),");
        b.AppendLine("    };");
        b.AppendLine();
        b.AppendLine("    public static readonly SemanticAttributeDescriptor[] Attributes =");
        b.AppendLine("    {");
        foreach (var r in rows)
        {
            foreach (var a in r.Required) b.AppendLine($"        new(\"{r.Key}\", \"{a}\", AttributeRequirement.Required),");
            foreach (var a in r.Recommended) b.AppendLine($"        new(\"{r.Key}\", \"{a}\", AttributeRequirement.Recommended),");
            foreach (var a in r.OptIn) b.AppendLine($"        new(\"{r.Key}\", \"{a}\", AttributeRequirement.OptIn),");
        }
        b.AppendLine("    };");
        b.AppendLine("    public static bool IsImplemented(string key)");
        b.AppendLine("    {");
        b.AppendLine("        foreach (var c in Capabilities) if (c.Key == key && c.Status == CapabilityStatus.Implemented) return true;");
        b.AppendLine("        return false;");
        b.AppendLine("    }");
        b.AppendLine("}");
        return b.ToString();
    }

    private static string Status(string s) => s switch
    {
        "implemented" => "Implemented",
        "control_bound" => "ControlBound",
        "option_bound" => "OptionBound",
        "unsupported_nativeaot" => "UnsupportedNativeAot",
        _ => "ResearchRequired",
    };

    private static string BuildSeedsSource(List<KeyValuePair<string, string>> seeds)
    {
        var b = new StringBuilder();
        b.AppendLine("// <auto-generated/> pre-compilation phase: semantic-convention seed index.");
        b.AppendLine($"namespace {Ns};");
        b.AppendLine("/// <summary>Seed map (property-name -> semantic-convention attribute). Lives in the compilation.</summary>");
        b.AppendLine("public static class SemanticSeeds");
        b.AppendLine("{");
        foreach (var s in seeds)
            b.AppendLine($"    public const string {s.Key} = \"{s.Value}\";");
        b.AppendLine($"    public const int SeedCount = {seeds.Count};");
        b.AppendLine("}");
        return b.ToString();
    }

    private readonly record struct Binding(string DeclaringType, string Property, string Attribute);

    private static List<Binding> InferBindings(Compilation compilation, Dictionary<string, string> seedMap)
    {
        var bindings = new List<Binding>();
        if (seedMap.Count == 0) return bindings;
        foreach (var type in EnumerateSourceTypes(compilation.GlobalNamespace))
        {
            foreach (var prop in type.GetMembers().OfType<IPropertySymbol>())
            {
                if (seedMap.TryGetValue(prop.Name, out var attr))
                    bindings.Add(new Binding(type.ToDisplayString(), prop.Name, attr));
            }
        }
        return bindings.OrderBy(x => x.DeclaringType, StringComparer.Ordinal).ThenBy(x => x.Property, StringComparer.Ordinal).ToList();
    }

    private static IEnumerable<INamedTypeSymbol> EnumerateSourceTypes(INamespaceSymbol ns)
    {
        foreach (var t in ns.GetTypeMembers())
            if (t.Locations.Any(l => l.IsInSource))
                yield return t;
        foreach (var child in ns.GetNamespaceMembers())
            foreach (var t in EnumerateSourceTypes(child))
                yield return t;
    }

    private static string BuildBindingSource(int boundCount, List<Binding> bindings)
    {
        var b = new StringBuilder();
        b.AppendLine("// <auto-generated/> standard phase: bound pre-compilation symbols + compile-time-only inference.");
        b.AppendLine($"namespace {Ns};");
        b.AppendLine("public static class ContractBinding");
        b.AppendLine("{");
        b.AppendLine($"    public const int BoundCapabilityCount = {boundCount};");
        b.AppendLine($"    public const int InferredAttributeCount = {bindings.Count};");
        b.AppendLine("    public static readonly (string Type, string Property, string Attribute)[] InferredBindings =");
        b.AppendLine("    {");
        foreach (var x in bindings)
            b.AppendLine($"        (\"{x.DeclaringType}\", \"{x.Property}\", \"{x.Attribute}\"),");
        b.AppendLine("    };");
        b.AppendLine("}");
        return b.ToString();
    }
}
