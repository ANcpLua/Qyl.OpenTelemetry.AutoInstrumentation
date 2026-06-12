using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Graph Ql.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedGraphQl);</code></example>
public static class QylInterceptedGraphQl
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity()
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.GraphQl,
            QylActivityNames.GraphQlExecute,
            ActivityKind.Internal,
            QylInstrumentationDomains.GraphQl);
        if (activity is null)
            return null;

        QylActivityTags.SetGraphQlOperationName(activity, "execute");
        return activity;
    }

    /// <summary>Runs the Record Execution Options runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordExecutionOptions(Activity? activity, string? operationName, string? document)
    {
        if (activity is null)
            return;

        if (!string.IsNullOrWhiteSpace(operationName))
            QylActivityTags.SetGraphQlOperationName(activity, operationName);

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

    /// <summary>Observes an asynchronous GraphQL operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
