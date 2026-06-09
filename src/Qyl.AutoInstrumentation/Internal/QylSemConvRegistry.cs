using System.Collections.Frozen;

namespace Qyl.AutoInstrumentation.Internal;

/// <summary>
/// The qyl semconv registry — the set of attribute keys recognised by the active semconv version.
///
/// <para>
/// The substrate-era code built this with <c>Assembly.Load</c> + <c>Type.GetFields</c> reflection
/// over the <c>Qyl.OpenTelemetry.SemanticConventions</c> packages at process startup. That path is
/// NOT AOT-safe (the trim/AOT analyzers reject <c>Assembly.GetTypes()</c>), so the build-time
/// source generator now emits a <c>FrozenSet&lt;string&gt;</c> partial from the
/// <c>Qyl.AutoInstrumentation.SourceGenerators</c> assembly. The fallback below keeps the file
/// compile-clean before the generator runs in a fresh checkout.
/// </para>
/// </summary>
internal static partial class QylSemConvRegistry
{
    /// <summary>
    /// All exact semconv attribute keys known at compile time. Populated by the source generator's
    /// <c>partial</c> contribution; qyl-owned compatibility keys are added locally because they are
    /// intentionally outside the upstream semconv registry.
    /// </summary>
    public static readonly FrozenSet<string> KnownKeys;

    private static readonly FrozenSet<string> KnownTemplatePrefixes;

    static QylSemConvRegistry()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        var templatePrefixes = new HashSet<string>(StringComparer.Ordinal);
        Contribute(keys, templatePrefixes);
        ContributeQylContractKeys(keys, templatePrefixes);

        KnownKeys = keys.ToFrozenSet(StringComparer.Ordinal);
        KnownTemplatePrefixes = templatePrefixes.ToFrozenSet(StringComparer.Ordinal);
    }

    static partial void Contribute(HashSet<string> keys, HashSet<string> templatePrefixes);

    public static bool IsKnownKey(string key)
    {
        if (KnownKeys.Contains(key))
            return true;

        foreach (var templatePrefix in KnownTemplatePrefixes)
        {
            if (key.StartsWith(templatePrefix, StringComparison.Ordinal))
                return true;
        }

        return false;
    }

    private static void ContributeQylContractKeys(HashSet<string> keys, HashSet<string> templatePrefixes)
    {
        keys.Add(QylSemanticAttributes.QylInstrumentationDomain);
        keys.Add(QylSemanticAttributes.QylConformanceVerdict);
        keys.Add(QylSemanticAttributes.LogSeverity);
        keys.Add(QylSemanticAttributes.LogEventName);

        templatePrefixes.Add(QylSemanticAttributes.HttpRequestHeaderPrefix);
        templatePrefixes.Add(QylSemanticAttributes.HttpResponseHeaderPrefix);
        templatePrefixes.Add(QylSemanticAttributes.GrpcRequestMetadataPrefix);
        templatePrefixes.Add(QylSemanticAttributes.GrpcResponseMetadataPrefix);
    }
}
