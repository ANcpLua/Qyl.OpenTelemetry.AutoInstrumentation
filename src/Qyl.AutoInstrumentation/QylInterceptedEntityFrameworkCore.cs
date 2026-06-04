using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedEntityFrameworkCore
{

    public static Activity? StartActivity(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.EntityFrameworkCore))
            return null;

        var activity = QylActivitySource.Source.StartActivity("EFCORE " + operationName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbEfCore);
        activity.SetTag(QylSemanticAttributes.DbOperationName, operationName);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, operationName);
        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
