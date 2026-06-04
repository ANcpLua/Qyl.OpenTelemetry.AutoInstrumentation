using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedExternalLogger
{
    public static Activity? StartActivity(string instrumentationId, string domain, string methodName)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);
        ArgumentNullException.ThrowIfNull(domain);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, instrumentationId))
            return null;

        var activity = QylActivitySource.Source.StartActivity(domain + " " + methodName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, domain);
        activity.SetTag(QylSemanticAttributes.LogSeverity, NormalizeSeverity(methodName));
        return activity;
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
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
