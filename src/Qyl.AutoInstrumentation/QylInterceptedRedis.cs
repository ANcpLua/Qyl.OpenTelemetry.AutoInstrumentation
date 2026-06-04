using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedRedis
{

    public static Activity? StartCommandActivity(string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.StackExchangeRedis))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Redis command", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbRedis);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemRedis);
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
