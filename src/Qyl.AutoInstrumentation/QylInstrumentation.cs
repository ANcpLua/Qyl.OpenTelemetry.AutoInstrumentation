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

        SemConvConformanceProcessor.EnsureListenerRegisteredIfEnabled();

        QylRuntimeProcessMetrics.Initialize();

        return true;
    }
}
