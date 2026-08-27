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

        if (OperationType(document, operationName) is { } operationType)
        {
            QylActivityTags.SetGraphQlOperationType(activity, operationType);
            activity.DisplayName = QylSpanNames.GraphQl(operationType);
        }

        QylSensitiveCapturePolicy.SetGraphQlDocument(activity, document);
    }

    /// <summary>Observes an asynchronous GraphQL operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    // The type of the operation the request executes: the definition operationName selects, else the
    // first operation; a shorthand document is a query. Fragments, strings, comments, variable lists,
    // directives, and everything inside a selection set are skipped; an unreadable document yields none.
    private static string? OperationType(string? document, string? operationName)
    {
        if (string.IsNullOrEmpty(document))
            return null;

        var depth = 0;
        var parentheses = 0;
        string? pendingType = null;
        string? pendingName = null;
        var nameClosed = false;
        var sawWord = false;
        var position = 0;
        while (position < document.Length)
        {
            var current = document[position];
            if (current is '#')
            {
                while (position < document.Length && document[position] is not '\n' and not '\r')
                    position++;
                continue;
            }

            if (current is '"')
            {
                if (!SkipString(document, ref position))
                    return null;
                continue;
            }

            if (current is '{')
            {
                if (depth is 0 && parentheses is 0)
                {
                    if (!sawWord)
                        return QylSemanticAttributes.GraphQlOperationTypeQuery;
                    if (pendingType is not null && (operationName is null || StringComparer.Ordinal.Equals(pendingName, operationName)))
                        return pendingType;
                }

                depth++;
                position++;
                continue;
            }

            if (current is '}')
            {
                if (--depth is 0)
                {
                    pendingType = null;
                    pendingName = null;
                    nameClosed = false;
                    sawWord = false;
                }

                position++;
                continue;
            }

            if (current is '(' or '@')
            {
                if (current is '(')
                    parentheses++;
                nameClosed = true;
                position++;
                continue;
            }

            if (current is ')')
            {
                parentheses--;
                position++;
                continue;
            }

            if (!(char.IsLetter(current) || current is '_'))
            {
                position++;
                continue;
            }

            var start = position;
            while (position < document.Length && (char.IsLetterOrDigit(document[position]) || document[position] is '_'))
                position++;
            if (depth is not 0 || parentheses is not 0)
                continue;

            sawWord = true;
            var word = document.AsSpan(start, position - start);
            var type = word switch
            {
                "query" => QylSemanticAttributes.GraphQlOperationTypeQuery,
                "mutation" => QylSemanticAttributes.GraphQlOperationTypeMutation,
                "subscription" => QylSemanticAttributes.GraphQlOperationTypeSubscription,
                _ => null,
            };
            if (type is not null)
            {
                pendingType = type;
                pendingName = null;
                nameClosed = false;
            }
            else if (pendingType is not null && pendingName is null && !nameClosed)
            {
                pendingName = word.ToString();
            }
        }

        return null;
    }

    // A block string ("""…""") or a string with backslash escapes; false when unterminated.
    private static bool SkipString(string document, ref int position)
    {
        if (string.CompareOrdinal(document, position, "\"\"\"", 0, 3) is 0)
        {
            var end = document.IndexOf("\"\"\"", position + 3, StringComparison.Ordinal);
            while (end > 0 && document[end - 1] is '\\')
                end = document.IndexOf("\"\"\"", end + 1, StringComparison.Ordinal);
            if (end < 0)
                return false;
            position = end + 3;
            return true;
        }

        position++;
        while (position < document.Length)
        {
            var current = document[position++];
            if (current is '\\')
            {
                position++;
                continue;
            }

            if (current is '"')
                return true;
            if (current is '\n' or '\r')
                return false;
        }

        return false;
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
