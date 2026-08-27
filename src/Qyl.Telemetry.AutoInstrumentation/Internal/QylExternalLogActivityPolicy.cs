using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylExternalLogActivityPolicy
{
    public static Activity? Start(string instrumentationId, string domain, string activityName, string methodName, string? severityName)
    {
        ArgumentNullException.ThrowIfNull(methodName);

        var activity = QylActivityFactory.StartLogActivity(
            instrumentationId,
            activityName,
            ActivityKind.Internal,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetLogSeverity(
            activity,
            NormalizeSeverity(string.IsNullOrWhiteSpace(severityName) ? methodName : severityName));
        return activity;
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
}
