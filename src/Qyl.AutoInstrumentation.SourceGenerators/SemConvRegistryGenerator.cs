using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace Qyl.AutoInstrumentation.SourceGenerators;

/// <summary>
/// AOT-native replacement for the substrate-era reflection registry.
///
/// <para>
/// The substrate-era M3 milestone built the conformance registry at process startup by
/// <c>Assembly.Load("Qyl.OpenTelemetry.SemanticConventions")</c> → <c>asm.GetTypes()</c> →
/// <c>type.GetFields()</c>. That path is fundamentally unsafe under NativeAOT and is rejected by
/// the trim/AOT analyzer (RUC/IL2026/IL3050).
/// </para>
///
/// <para>
/// This incremental generator is the swap: it ingests the referenced semconv assembly's metadata
/// at compile time and emits a <c>partial</c> contribution to
/// <c>Qyl.AutoInstrumentation.Internal.QylSemConvRegistry</c> that bakes every known key into a
/// build-time <c>FrozenSet&lt;string&gt;</c>.
/// </para>
///
/// <para>
/// Skeleton: the generator is wired into the build pipeline but emits no code yet (M1 of the new
/// substrate). The next milestone implements the metadata read + emission.
/// </para>
/// </summary>
[Generator(LanguageNames.CSharp)]
public sealed class SemConvRegistryGenerator : IIncrementalGenerator
{
    /// <inheritdoc/>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var registries = context.CompilationProvider.Select(static (compilation, _) => CollectRegistry(compilation));

        context.RegisterSourceOutput(registries, static (sourceContext, registry) =>
        {
            if (!registry.ShouldEmit)
                return;

            sourceContext.AddSource(
                "QylSemConvRegistry.g.cs",
                SourceText.From(EmitRegistry(registry), Encoding.UTF8));
        });
    }

    private static SemConvRegistryModel CollectRegistry(Compilation compilation)
    {
        if (!string.Equals(compilation.AssemblyName, "Qyl.AutoInstrumentation", StringComparison.Ordinal))
            return SemConvRegistryModel.Empty;

        var keys = new HashSet<string>(StringComparer.Ordinal);
        var templatePrefixes = new HashSet<string>(StringComparer.Ordinal);
        CollectFromNamespace(compilation.GlobalNamespace, keys, templatePrefixes);

        return new SemConvRegistryModel(
            true,
            ToSortedImmutableArray(keys),
            ToSortedImmutableArray(templatePrefixes));
    }

    private static void CollectFromNamespace(
        INamespaceSymbol namespaceSymbol,
        HashSet<string> keys,
        HashSet<string> templatePrefixes)
    {
        foreach (var type in namespaceSymbol.GetTypeMembers())
            CollectFromType(type, keys, templatePrefixes);

        foreach (var child in namespaceSymbol.GetNamespaceMembers())
            CollectFromNamespace(child, keys, templatePrefixes);
    }

    private static void CollectFromType(
        INamedTypeSymbol type,
        HashSet<string> keys,
        HashSet<string> templatePrefixes)
    {
        if (IsSemConvAttributesType(type))
        {
            foreach (var field in type.GetMembers().OfType<IFieldSymbol>())
            {
                if (!field.HasConstantValue ||
                    field.ConstantValue is not string value ||
                    !IsAttributeKey(value))
                {
                    continue;
                }

                keys.Add(value);
                if (IsTemplateAttribute(field, value))
                    templatePrefixes.Add(value + ".");
            }
        }

        foreach (var nestedType in type.GetTypeMembers())
            CollectFromType(nestedType, keys, templatePrefixes);
    }

    private static bool IsSemConvAttributesType(INamedTypeSymbol type)
    {
        if (!type.Name.EndsWith("Attributes", StringComparison.Ordinal))
            return false;

        var namespaceName = type.ContainingNamespace.ToDisplayString();
        return namespaceName.StartsWith(
                   "Qyl.OpenTelemetry.SemanticConventions.Attributes.",
                   StringComparison.Ordinal) ||
               namespaceName.StartsWith(
                   "Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.",
                   StringComparison.Ordinal);
    }

    private static bool IsAttributeKey(string value)
        => value.Length > 0 &&
           value.IndexOf('.') >= 0 &&
           value.IndexOf(' ') < 0 &&
           value[0] is not '.' &&
           value[value.Length - 1] is not '.';

    private static bool IsTemplateAttribute(IFieldSymbol field, string value)
        => value.EndsWith(".header", StringComparison.Ordinal) ||
           value.EndsWith(".metadata", StringComparison.Ordinal) ||
           field.Name.EndsWith("Header", StringComparison.Ordinal) ||
           field.Name.EndsWith("Metadata", StringComparison.Ordinal);

    private static ImmutableArray<string> ToSortedImmutableArray(HashSet<string> values)
    {
        var builder = ImmutableArray.CreateBuilder<string>(values.Count);
        foreach (var value in values)
            builder.Add(value);

        builder.Sort(StringComparer.Ordinal);
        return builder.ToImmutable();
    }

    private static string EmitRegistry(SemConvRegistryModel registry)
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("namespace Qyl.AutoInstrumentation.Internal;");
        builder.AppendLine();
        builder.AppendLine("internal static partial class QylSemConvRegistry");
        builder.AppendLine("{");
        builder.AppendLine("    static partial void Contribute(");
        builder.AppendLine("        global::System.Collections.Generic.HashSet<string> keys,");
        builder.AppendLine("        global::System.Collections.Generic.HashSet<string> templatePrefixes)");
        builder.AppendLine("    {");

        foreach (var key in registry.Keys)
        {
            builder.Append("        keys.Add(");
            AppendStringLiteral(builder, key);
            builder.AppendLine(");");
        }

        if (registry.TemplatePrefixes.Length > 0)
            builder.AppendLine();

        foreach (var templatePrefix in registry.TemplatePrefixes)
        {
            builder.Append("        templatePrefixes.Add(");
            AppendStringLiteral(builder, templatePrefix);
            builder.AppendLine(");");
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }

    private static void AppendStringLiteral(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        builder.Append('"');
    }

    private sealed class SemConvRegistryModel
    {
        public static readonly SemConvRegistryModel Empty =
            new(false, ImmutableArray<string>.Empty, ImmutableArray<string>.Empty);

        public SemConvRegistryModel(bool shouldEmit, ImmutableArray<string> keys, ImmutableArray<string> templatePrefixes)
        {
            ShouldEmit = shouldEmit;
            Keys = keys;
            TemplatePrefixes = templatePrefixes;
        }

        public bool ShouldEmit { get; }

        public ImmutableArray<string> Keys { get; }

        public ImmutableArray<string> TemplatePrefixes { get; }
    }
}
