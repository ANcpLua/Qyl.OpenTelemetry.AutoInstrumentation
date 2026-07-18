using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted External Logger.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedExternalLogger
{
    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string instrumentationId, string domain, string methodName, string? severityName)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(methodName);

        var activity = QylActivityFactory.StartLogActivity(
            instrumentationId,
            GetActivityName(instrumentationId),
            ActivityKind.Internal,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetLogSeverity(
            activity,
            NormalizeSeverity(string.IsNullOrWhiteSpace(severityName) ? methodName : severityName));
        return activity;
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }

    private static string NormalizeSeverity(string methodName)
    {
        if (methodName.StartsWith("Trace", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityTrace;
        if (methodName.StartsWith("Debug", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityDebug;
        if (methodName.StartsWith("Info", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityInformation;
        if (methodName.StartsWith("Warn", StringComparison.Ordinal) || methodName.StartsWith("Warning", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityWarning;
        if (methodName.StartsWith("Error", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityError;
        if (methodName.StartsWith("Fatal", StringComparison.Ordinal) || methodName.StartsWith("Critical", StringComparison.Ordinal))
            return QylSemanticAttributes.LogSeverityCritical;

        return QylSemanticAttributes.LogSeverityOther;
    }

    private static string GetActivityName(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.NLog => "NLog log",
            QylAutoInstrumentationIds.Log4Net => "log4net log",
            _ => "external log",
        };
}
