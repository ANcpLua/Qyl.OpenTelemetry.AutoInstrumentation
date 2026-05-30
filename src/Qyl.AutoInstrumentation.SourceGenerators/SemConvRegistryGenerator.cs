using Microsoft.CodeAnalysis;

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
        // M1-new-substrate: prove the generator loads under the new build, no emission yet.
        // M2-new-substrate: read referenced Qyl.OpenTelemetry.SemanticConventions metadata, emit
        //                   `partial void Contribute(HashSet<string> keys)` populating every key.
    }
}
