namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Read-only access to this build's <b>Telemetry Capability Graph (TCG)</b> — the complete,
/// generated, provenance-tagged manifest of every telemetry capability this binary can produce.
/// </summary>
/// <remarks>
/// The TCG is generated deterministically from the instrumentation contract (it is not sampled and
/// cannot drift), so an external entity can learn the full possible OpenTelemetry surface of this
/// binary without observing exported telemetry over time. The document shape and the vendor-neutral
/// exchange schema are described in <c>docs/TELEMETRY_CAPABILITY_GRAPH.md</c> and
/// <c>docs/schema/telemetry-capability-graph.schema.json</c>.
///
/// <para>
/// The manifest body is filled by <c>TelemetryCapabilityGraphGenerator</c> via the generated
/// <see cref="Contribute"/> partial. That partial is elided when the generator does not run, so this
/// public surface always compiles; in that degraded case <see cref="Json"/> is <c>"{}"</c> and
/// <see cref="CapabilityCount"/> is <c>0</c>. A normal build of the core assembly bakes the real
/// manifest.
/// </para>
/// </remarks>
public static partial class QylTelemetryCapabilityGraph
{
    private static readonly (string Json, int CapabilityCount) Data = Build();

    /// <summary>The Telemetry Capability Graph serialized as compact JSON (schema <see cref="SchemaVersion"/>).</summary>
    public static string Json => Data.Json;

    /// <summary>Semantic version of the <see cref="Json"/> document format.</summary>
    public static string SchemaVersion => "0.1.0-draft";

    /// <summary>Number of capabilities declared in <see cref="Json"/>.</summary>
    public static int CapabilityCount => Data.CapabilityCount;

    static partial void Contribute(ref string json, ref int capabilityCount);

    private static (string Json, int CapabilityCount) Build()
    {
        var json = "{}";
        var capabilityCount = 0;
        Contribute(ref json, ref capabilityCount);
        return (json, capabilityCount);
    }
}
