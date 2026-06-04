using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedElastic
{
    private const string ElasticsearchDomain = "db.elasticsearch";
    private const string ElasticTransportDomain = "elastic.transport";

    public static Activity? StartActivity(string instrumentationId, string clientType, string methodName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, instrumentationId))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Elasticsearch " + methodName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(
            QylSemanticAttributes.QylInstrumentationDomain,
            string.Equals(instrumentationId, QylAutoInstrumentationIds.ElasticTransport, StringComparison.Ordinal)
                ? ElasticTransportDomain
                : ElasticsearchDomain);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemElasticsearch);
        activity.SetTag(QylSemanticAttributes.DbOperationName, methodName);
        activity.SetTag(QylSemanticAttributes.RpcService, clientType);
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
