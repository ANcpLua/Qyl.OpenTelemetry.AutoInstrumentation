using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Graph Ql.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedGraphQl
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity()
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.GraphQl,
            QylSpanNames.GraphQl(null),
            ActivityKind.Internal,
            QylInstrumentationDomains.GraphQl);
        if (activity is null)
            return null;

        return activity;
    }

    /// <summary>Runs the Record Execution Options runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordExecutionOptions(Activity? activity, string? operationName, string? document)
    {
        if (activity is null)
            return;

        if (!string.IsNullOrWhiteSpace(operationName))
            QylActivityTags.SetGraphQlOperationName(activity, operationName);

        if (OperationType(document) is { } operationType)
        {
            QylActivityTags.SetGraphQlOperationType(activity, operationType);
            activity.DisplayName = QylSpanNames.GraphQl(operationType);
        }

        QylSensitiveCapturePolicy.SetGraphQlDocument(activity, document);
    }

    /// <summary>Observes an asynchronous GraphQL operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    // The document's leading definition keyword; a shorthand document starts with '{' and is a query.
    private static string? OperationType(string? document)
    {
        if (document is null)
            return null;

        var position = 0;
        while (position < document.Length)
        {
            var current = document[position];
            if (char.IsWhiteSpace(current) || current is ',')
            {
                position++;
                continue;
            }

            if (current is '#')
            {
                while (position < document.Length && document[position] is not '\n')
                    position++;
                continue;
            }

            break;
        }

        if (position >= document.Length)
            return null;

        if (document[position] is '{')
            return QylSemanticAttributes.GraphQlOperationTypeQuery;

        var start = position;
        while (position < document.Length && char.IsLetter(document[position]))
            position++;

        return document.AsSpan(start, position - start) switch
        {
            "query" => QylSemanticAttributes.GraphQlOperationTypeQuery,
            "mutation" => QylSemanticAttributes.GraphQlOperationTypeMutation,
            "subscription" => QylSemanticAttributes.GraphQlOperationTypeSubscription,
            _ => null,
        };
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
