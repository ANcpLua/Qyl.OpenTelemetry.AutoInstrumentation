using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Elastic.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedElastic);</code></example>
public static class QylInterceptedElastic
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string instrumentationId, string methodName)
    {
        var operation = NormalizeOperation(methodName);
        var isElasticTransport = string.Equals(instrumentationId, QylAutoInstrumentationIds.ElasticTransport, StringComparison.Ordinal);
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            isElasticTransport ? "Elastic transport request" : "Elasticsearch request",
            ActivityKind.Client,
            isElasticTransport ? QylInstrumentationDomains.ElasticTransport : QylInstrumentationDomains.DbElasticsearch);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            QylSemanticAttributes.DbSystemElasticsearch,
            operation,
            operation);
        return activity;
    }

    /// <summary>Runs the Observe Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task ObserveAsync(Task? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Observes an asynchronous operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
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
