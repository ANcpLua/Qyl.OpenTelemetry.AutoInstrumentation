using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;

namespace Qyl.AutoInstrumentation.Plugin;

/// <summary>
/// FIRST qyl-authored code in the live pipeline (milestone M2).
///
/// Loaded by the reused OTel auto-instrumentation substrate via:
///   OTEL_DOTNET_AUTO_PLUGINS="Qyl.AutoInstrumentation.Plugin.Plugin, Qyl.AutoInstrumentation.Plugin"
///
/// It adds a span processor that checks emitted attribute keys against the qyl semconv
/// registry. It must NOT alter the emitted span (Gate A) or app behavior (Gate B) — the
/// conformance verdict is side-channelled to a file, never stdout/stderr.
/// </summary>
public class Plugin
{
    /// <summary>Substrate plugin hook: runs after the SDK's own tracer configuration.</summary>
    public TracerProviderBuilder AfterConfigureTracerProvider(TracerProviderBuilder builder)
        => builder.AddProcessor(new SemConvConformanceProcessor());

    /// <summary>Substrate plugin hook: register qyl's self-telemetry meter (M4) so its
    /// conformance metrics flow through the configured metrics exporter.</summary>
    public MeterProviderBuilder AfterConfigureMeterProvider(MeterProviderBuilder builder)
        => builder.AddMeter(QylSelfTelemetry.MeterName);
}
