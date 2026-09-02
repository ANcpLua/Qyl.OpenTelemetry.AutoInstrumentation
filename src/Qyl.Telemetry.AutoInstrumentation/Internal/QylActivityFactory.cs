using System.Diagnostics;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;

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

        activity.SetTag(QylAttributes.InstrumentationDomain, instrumentationDomain);
        return activity;
    }
}
