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
            return "Trace";
        if (methodName.StartsWith("Debug", StringComparison.Ordinal))
            return "Debug";
        if (methodName.StartsWith("Info", StringComparison.Ordinal))
            return "Information";
        if (methodName.StartsWith("Warn", StringComparison.Ordinal) || methodName.StartsWith("Warning", StringComparison.Ordinal))
            return "Warning";
        if (methodName.StartsWith("Error", StringComparison.Ordinal))
            return "Error";
        if (methodName.StartsWith("Fatal", StringComparison.Ordinal) || methodName.StartsWith("Critical", StringComparison.Ordinal))
            return "Critical";

        return methodName;
    }
}
