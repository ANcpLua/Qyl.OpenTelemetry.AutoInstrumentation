using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylActivityFactory
{
    public static Activity? StartTraceActivity(
        string instrumentationId,
        string activityName,
        ActivityKind activityKind,
        string instrumentationDomain)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, instrumentationId))
            return null;

        var activity = QylActivitySource.StartActivity(activityName, activityKind);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, instrumentationDomain);
        return activity;
    }
}
