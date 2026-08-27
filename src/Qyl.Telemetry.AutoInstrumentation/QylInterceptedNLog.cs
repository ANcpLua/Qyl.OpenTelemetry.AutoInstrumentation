using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>NLog log-as-span activities.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.NLog, QylInstrumentationDomains.LogNLog, Signal = QylAutoInstrumentationSignal.Logs)]
[QylIntercept(
    "NLog.Logger",
    "Log",
    "Trace", "TraceFormat", "Debug", "DebugFormat", "Info", "InfoFormat", "Warn", "WarnFormat", "Warning", "WarningFormat",
    "Error", "ErrorFormat", "Fatal", "FatalFormat", "Critical", "CriticalFormat",
    Shape = QylShapes.ExternalLogger,
    Body = QylInterceptorBody.ExternalLog,
    Start = nameof(Log))]
public static class QylInterceptedNLog
{
    private const string ActivityName = "NLog log";

    /// <summary>Starts the log activity for the intercepted NLog call.</summary>
    public static Activity? Log([QylFromMethodName] string methodName, [QylFromShape] string? severityName)
        => QylExternalLogActivityPolicy.Start(QylAutoInstrumentationIds.NLog, QylInstrumentationDomains.LogNLog, ActivityName, methodName, severityName);
}
