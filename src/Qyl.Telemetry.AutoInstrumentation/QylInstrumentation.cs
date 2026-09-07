namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>
/// Public API facade for the qyl AOT-native instrumentation core. This is the only entry point a
/// consuming app needs — wire it into the app's startup (or let the
/// <c>[ModuleInitializer]</c> in <c>Qyl.Telemetry.AutoInstrumentation.Hosting</c> do it automatically).
/// </summary>
internal static class QylInstrumentation
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
    /// Applies the process-wide runtime switches qyl instrumentation depends on.
    /// </summary>
    /// <returns><c>true</c> on the first activation, <c>false</c> on subsequent calls.</returns>
    public static bool Activate()
    {
        if (Interlocked.Exchange(ref _activated, 1) == 1)
            return false;

        // The outbound HTTP span is the BCL's own, and the BCL redacts the query of its url.full to
        // "*" by itself. qyl does not rewrite what a library emitted, so the only thing left to bind
        // is the consumer's opt-out: OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION
        // now flips the runtime's own switch instead of a qyl-owned redaction that no longer exists.
        if (QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled)
            AppContext.SetSwitch("System.Net.Http.DisableUriRedaction", true);

        if (QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(
                QylAutoInstrumentationSignal.Traces,
                QylAutoInstrumentationIds.Azure))
        {
            AppContext.SetSwitch("Azure.Experimental.EnableActivitySource", true);
        }

        return true;
    }
}
