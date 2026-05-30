using System.Collections.Frozen;

namespace Qyl.AutoInstrumentation.Internal;

/// <summary>
/// The qyl semconv registry — the set of attribute keys recognised by the active semconv version.
///
/// <para>
/// The substrate-era code built this with <c>Assembly.Load</c> + <c>Type.GetFields</c> reflection
/// over the <c>Qyl.OpenTelemetry.SemanticConventions</c> packages at process startup. That path is
/// NOT AOT-safe (the trim/AOT analyzers reject <c>Assembly.GetTypes()</c>), so the build-time
/// source generator now emits a <c>FrozenSet&lt;string&gt;</c> partial via
/// <see cref="Qyl.AutoInstrumentation.SourceGenerators"/>. The fallback below keeps the file
/// compile-clean before the generator runs in a fresh checkout.
/// </para>
/// </summary>
internal static partial class QylSemConvRegistry
{
    /// <summary>
    /// All semconv attribute keys known at compile time. Populated by the source generator's
    /// <c>partial</c> contribution; falls back to an empty <see cref="FrozenSet{T}"/> in skeleton
    /// builds so the rest of the assembly stays compile-clean.
    /// </summary>
    public static readonly FrozenSet<string> KnownKeys = BuildKnownKeys();

    static partial void Contribute(HashSet<string> keys);

    private static FrozenSet<string> BuildKnownKeys()
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        Contribute(keys);
        return keys.ToFrozenSet(StringComparer.Ordinal);
    }
}
