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
/// <c>docs/schema/telemetry-capability-graph.schema.json</c>. Backing data is the generated
/// <c>Internal.QylTelemetryCapabilityGraphData</c>.
/// </remarks>
public static class QylTelemetryCapabilityGraph
{
    /// <summary>The Telemetry Capability Graph serialized as compact JSON (schema <see cref="SchemaVersion"/>).</summary>
    public static string Json => Internal.QylTelemetryCapabilityGraphData.Json;

    /// <summary>Semantic version of the <see cref="Json"/> document format.</summary>
    public static string SchemaVersion => Internal.QylTelemetryCapabilityGraphData.SchemaVersion;

    /// <summary>Number of capabilities declared in <see cref="Json"/>.</summary>
    public static int CapabilityCount => Internal.QylTelemetryCapabilityGraphData.CapabilityCount;
}
