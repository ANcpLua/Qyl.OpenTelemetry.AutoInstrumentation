using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Public API facade for the qyl AOT-native instrumentation core. This is the only entry point a
/// consuming app needs — wire it into the app's startup (or let the
/// <c>[ModuleInitializer]</c> in <c>Qyl.OpenTelemetry.AutoInstrumentation.Hosting</c> do it automatically).
/// </summary>
public static class QylInstrumentation
{
    /// <summary>
    /// Instrumentation-scope version stamped on every qyl-emitted span/metric (via
    /// <see cref="QylActivitySource"/>). Baked at build time from the root
    /// <c>Directory.Build.props</c> <c>&lt;Version&gt;</c> — which CI packs unchanged and uses for the
    /// post-verification <c>v*</c> tag — via the generated <c>QylVersionInfo</c> const (see the
    /// <c>GenerateQylVersionInfo</c> target in the core project). No reflection; it always matches
    /// the shipped package and is never hand-maintained.
    /// </summary>
    public static readonly string Version = QylVersionInfo.Version;

    private static int _activated;

    /// <summary>
    /// Activate qyl runtime metrics.
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

        QylRuntimeProcessMetrics.Initialize();

        return true;
    }
}
