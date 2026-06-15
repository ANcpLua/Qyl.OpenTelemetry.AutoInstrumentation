using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Public API facade for the qyl AOT-native instrumentation core. This is the only entry point a
/// consuming app needs — wire it into the app's startup (or let the
/// <c>[ModuleInitializer]</c> in <c>Qyl.OpenTelemetry.AutoInstrumentation.Hosting</c> do it automatically).
/// </summary>
public static class QylInstrumentation
{
    /// <summary>Library version, baked at build time by the root <c>Directory.Build.props</c>.</summary>
    public const string Version = "0.3.0-pre.1";

    private static int _activated;

    /// <summary>
    /// Activate qyl runtime metrics and the development-only semconv conformance listener when it
    /// is explicitly opted in.
    /// </summary>
    /// <returns><c>true</c> on the first activation, <c>false</c> on subsequent calls.</returns>
    public static bool Activate()
    {
        if (Interlocked.Exchange(ref _activated, 1) == 1)
            return false;

        // The BCL pre-redacts query strings in its distributed-tracing tags to "*", destroying
        // the information qyl needs to emit upstream-OTel-shaped url.full values (per-value
        // "key=Redacted" redaction, raw only behind the upstream redaction-disable flag). qyl
        // owns telemetry in a zero-code app, so take the raw URI and redact it itself.
        AppContext.SetSwitch("System.Net.Http.DisableUriRedaction", true);

        SemConvConformanceProcessor.EnsureListenerRegisteredIfEnabled();

        QylRuntimeProcessMetrics.Initialize();

        return true;
    }
}
