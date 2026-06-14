using System.Diagnostics;

namespace Qyl.AutoInstrumentation.Internal;

internal static class QylActivityFactory
{
    public static Activity? StartTraceActivity(
        string instrumentationId,
        string activityName,
        ActivityKind activityKind,
        string instrumentationDomain)
        => StartActivity(
            QylAutoInstrumentationSignal.Traces,
            instrumentationId,
            activityName,
            activityKind,
            instrumentationDomain);

    public static Activity? StartLogActivity(
        string instrumentationId,
        string activityName,
        ActivityKind activityKind,
        string instrumentationDomain)
        => StartActivity(
            QylAutoInstrumentationSignal.Logs,
            instrumentationId,
            activityName,
            activityKind,
            instrumentationDomain);

    private static Activity? StartActivity(
        QylAutoInstrumentationSignal signal,
        string instrumentationId,
        string activityName,
        ActivityKind activityKind,
        string instrumentationDomain)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(signal, instrumentationId))
            return null;

        var activity = QylActivitySource.StartActivity(activityName, activityKind);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, instrumentationDomain);
        return activity;
    }
}
