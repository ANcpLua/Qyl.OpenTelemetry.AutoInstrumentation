using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedElastic
{

    public static Activity? StartActivity(string instrumentationId, string methodName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, instrumentationId))
            return null;

        var operation = NormalizeOperation(methodName);
        var activityName = string.Equals(instrumentationId, QylAutoInstrumentationIds.ElasticTransport, StringComparison.Ordinal)
            ? "Elastic transport request"
            : "Elasticsearch request";
        var activity = QylActivitySource.StartActivity(activityName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(
            QylSemanticAttributes.QylInstrumentationDomain,
            string.Equals(instrumentationId, QylAutoInstrumentationIds.ElasticTransport, StringComparison.Ordinal)
                ? QylInstrumentationDomains.ElasticTransport
                : QylInstrumentationDomains.DbElasticsearch);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemElasticsearch);
        activity.SetTag(QylSemanticAttributes.DbOperationName, operation);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, operation);
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

    private static string NormalizeOperation(string methodName)
        => methodName switch
        {
            "Request" or "RequestAsync" => "request",
            "Search" or "SearchAsync" => "search",
            "Index" or "IndexAsync" => "index",
            "Create" or "CreateAsync" => "create",
            "Update" or "UpdateAsync" => "update",
            "Delete" or "DeleteAsync" => "delete",
            "Bulk" or "BulkAsync" => "bulk",
            "Get" or "GetAsync" => "get",
            "Count" or "CountAsync" => "count",
            "Exists" or "ExistsAsync" => "exists",
            "MultiGet" or "MultiGetAsync" => "mget",
            "MultiSearch" or "MultiSearchAsync" => "msearch",
            _ => "request",
        };
}
