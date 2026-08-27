using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylElasticActivityPolicy
{
    public static Activity? Start(string instrumentationId, string domain, string methodName)
    {
        var operation = NormalizeOperation(methodName);
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            QylSpanNames.Db(operation, QylSemanticAttributes.DbSystemElasticsearch),
            ActivityKind.Client,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            QylSemanticAttributes.DbSystemElasticsearch,
            operation,
            operation);
        return activity;
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
