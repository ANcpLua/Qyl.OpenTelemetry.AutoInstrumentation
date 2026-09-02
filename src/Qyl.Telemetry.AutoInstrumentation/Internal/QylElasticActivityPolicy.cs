using System.Diagnostics;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylElasticActivityPolicy
{
    public static Activity? Start(string instrumentationId, string domain, string methodName)
    {
        var operation = NormalizeOperation(methodName);
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            QylSpanNames.Db(operation, DbIncubatingAttributes.SystemNameValues.Elasticsearch),
            ActivityKind.Client,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            DbIncubatingAttributes.SystemNameValues.Elasticsearch,
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
