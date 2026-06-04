using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedRedis
{
    private const string RedisDomain = "db.redis";

    public static Activity? StartStringGetActivity(string? key)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.StackExchangeRedis))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Redis " + QylSemanticAttributes.DbOperationNameGet, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, RedisDomain);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemRedis);
        activity.SetTag(QylSemanticAttributes.DbOperationName, QylSemanticAttributes.DbOperationNameGet);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, QylSemanticAttributes.DbOperationNameGet);

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
