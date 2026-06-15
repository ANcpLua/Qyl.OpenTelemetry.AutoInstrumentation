using System.Diagnostics.Metrics;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// qyl self-telemetry — a qyl-owned <see cref="Meter"/> whose instruments report the runtime's own
/// conformance activity. Preserved from the substrate-era M4 milestone; the contract is unchanged
/// because the instrument is a primitive of <c>System.Diagnostics.Metrics</c>, which is AOT-safe.
/// </summary>
public static class QylSelfTelemetry
{
    /// <summary>Meter name. Mirror this in <c>AddMeter(...)</c> on a MeterProvider.</summary>
    public const string MeterName = "Qyl.OpenTelemetry.AutoInstrumentation";

    private static readonly Meter Meter = new(MeterName);

    /// <summary>
    /// Counts emitted attribute keys checked against the qyl semconv registry, dimensioned by
    /// verdict (<c>ok</c> | <c>unknown</c>). The <c>verdict=unknown</c> slice is the conformance-
    /// violation signal that drives <c>--strict</c> gates downstream.
    /// </summary>
    public static readonly Counter<long> AttributeChecks = Meter.CreateCounter<long>(
        QylMetricNames.QylSemConvAttributeChecks,
        unit: "{check}",
        description: "Emitted attribute keys checked against the qyl semconv registry, by verdict.");

    /// <summary>Counts conformance-processor failures swallowed to preserve app behavior.</summary>
    public static readonly Counter<long> ConformanceProcessorFailures = Meter.CreateCounter<long>(
        QylMetricNames.QylSemConvProcessorFailures,
        unit: "{failure}",
        description: "Conformance processor failures swallowed to keep qyl instrumentation app-invisible.");
}
