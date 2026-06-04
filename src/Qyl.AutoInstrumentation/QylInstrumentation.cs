using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>
/// Public API facade for the qyl AOT-native instrumentation core. This is the only entry point a
/// consuming app needs — wire it into the app's startup (or let the
/// <c>[ModuleInitializer]</c> in <c>Qyl.AutoInstrumentation.Hosting</c> do it automatically).
/// </summary>
public static class QylInstrumentation
{
    /// <summary>Library version, baked at build time by the root <c>Directory.Build.props</c>.</summary>
    public const string Version = "0.2.0-pre.1";

    private static int _activated;

    /// <summary>
    /// Activate qyl: subscribe an <see cref="ActivityListener"/> that runs the M3 semconv-
    /// conformance check on every emitted span from the qyl ActivitySource. Idempotent.
    /// </summary>
    /// <returns><c>true</c> on the first activation, <c>false</c> on subsequent calls.</returns>
    public static bool Activate()
    {
        if (Interlocked.Exchange(ref _activated, 1) == 1)
            return false;

        ActivitySource.AddActivityListener(new ActivityListener
        {
            ShouldListenTo = static source => source.Name == QylActivitySource.Name,
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = SemConvConformanceProcessor.OnActivityStopped,
        });

        QylRuntimeProcessMetrics.Initialize();

        return true;
    }
}
