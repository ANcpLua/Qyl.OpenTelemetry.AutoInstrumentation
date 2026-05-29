using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation.Plugin;

/// <summary>
/// qyl self-telemetry (M4). A qyl-owned <see cref="Meter"/> whose instruments report the
/// runtime's own conformance activity. Registered with the substrate MeterProvider via the
/// plugin's AfterConfigureMeterProvider hook, so it flows through the configured metrics
/// exporter. First use of the metrics pipeline (M1–M3 were traces-only).
/// </summary>
internal static class QylSelfTelemetry
{
    public const string MeterName = "Qyl.AutoInstrumentation";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Counts emitted attribute keys checked against the qyl semconv registry, dimensioned by
    /// verdict (ok|unknown). The verdict=unknown slice is the conformance-violation signal.
    /// </summary>
    public static readonly Counter<long> AttributeChecks = Meter.CreateCounter<long>(
        "qyl.semconv.attribute.checks",
        unit: "{check}",
        description: "Emitted attribute keys checked against the qyl semconv registry, by verdict.");
}
