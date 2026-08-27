using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>log4net log-as-span activities.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.Log4Net, QylInstrumentationDomains.LogLog4Net, Signal = QylAutoInstrumentationSignal.Logs)]
[QylIntercept(
    "log4net.ILog",
    "Log",
    "Trace", "TraceFormat", "Debug", "DebugFormat", "Info", "InfoFormat", "Warn", "WarnFormat", "Warning", "WarningFormat",
    "Error", "ErrorFormat", "Fatal", "FatalFormat", "Critical", "CriticalFormat",
    Shape = QylShapes.ExternalLogger,
    Body = QylInterceptorBody.ExternalLog,
    Start = nameof(Log))]
[QylIntercept(
    "log4net.Core.ILogger",
    "Log",
    "Trace", "TraceFormat", "Debug", "DebugFormat", "Info", "InfoFormat", "Warn", "WarnFormat", "Warning", "WarningFormat",
    "Error", "ErrorFormat", "Fatal", "FatalFormat", "Critical", "CriticalFormat",
    Shape = QylShapes.ExternalLogger,
    Body = QylInterceptorBody.ExternalLog,
    Start = nameof(Log))]
public static class QylInterceptedLog4Net
{
    private const string ActivityName = "log4net log";

    /// <summary>Starts the log activity for the intercepted log4net call.</summary>
    public static Activity? Log([QylFromMethodName] string methodName, [QylFromShape] string? severityName)
        => QylExternalLogActivityPolicy.Start(QylAutoInstrumentationIds.Log4Net, QylInstrumentationDomains.LogLog4Net, ActivityName, methodName, severityName);
}
