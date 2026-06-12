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

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Observe Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task ObserveAsync(Task? task, Activity? activity)
    {
        if (activity is null || task is null)
        {
            activity?.Dispose();
            return task!;
        }

        return ObserveSlowAsync(task, activity);
    }

    /// <summary>Observes an asynchronous Elastic operation and records qyl success or exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
    {
        if (activity is null || task is null)
        {
            activity?.Dispose();
            return task!;
        }

        return ObserveSlowAsync(task, activity);
    }

    private static async Task ObserveSlowAsync(Task task, Activity activity)
    {
        try
        {
            await task.ConfigureAwait(false);
            RecordSuccess(activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private static async Task<T> ObserveSlowAsync<T>(Task<T> task, Activity activity)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            RecordSuccess(activity);
            return result;
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

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
