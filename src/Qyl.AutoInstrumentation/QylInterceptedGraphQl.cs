using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Graph Ql.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedGraphQl);</code></example>
public static class QylInterceptedGraphQl
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity()
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GraphQl))
            return null;

        if (QylActivitySource.StartActivity(QylActivityNames.GraphQlExecute, ActivityKind.Internal) is not { } activity)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.GraphQl);
        activity.SetTag(QylSemanticAttributes.GraphQlOperationName, "execute");
        return activity;
    }

    /// <summary>Runs the Record Execution Options runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordExecutionOptions(Activity? activity, string? operationName, string? document)
    {
        if (activity is null)
            return;

        if (!string.IsNullOrWhiteSpace(operationName))
            activity.SetTag(QylSemanticAttributes.GraphQlOperationName, operationName);

        if (QylAutoInstrumentationOptions.Current.GraphQlSetDocument &&
            !string.IsNullOrWhiteSpace(document))
        {
            activity.SetTag(QylSemanticAttributes.GraphQlDocument, document);
        }
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Observes an asynchronous GraphQL operation and records qyl success or exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
    {
        if (activity is null || task is null)
        {
            activity?.Dispose();
            return task!;
        }

        return ObserveSlowAsync(task, activity);
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
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
