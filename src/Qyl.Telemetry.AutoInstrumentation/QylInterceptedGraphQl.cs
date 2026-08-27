using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>GraphQL.NET document execution spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.GraphQl, QylInstrumentationDomains.GraphQl)]
[QylIntercept("GraphQL.IDocumentExecuter", "ExecuteAsync", Shape = QylShapes.GraphQlExecute, Start = nameof(Execute), Enrich = nameof(RecordExecutionOptions), ObserveAsync = true)]
public static class QylInterceptedGraphQl
{
    /// <summary>Starts the internal span; the operation type names it once the document is read.</summary>
    public static Activity? Execute()
        => QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.GraphQl,
            QylSpanNames.GraphQl(null),
            ActivityKind.Internal,
            QylInstrumentationDomains.GraphQl);

    /// <summary>Records the operation name and type, and the document behind its opt-in.</summary>
    public static void RecordExecutionOptions(
        Activity? activity,
        [QylFromArgument(0, Type = "GraphQL.ExecutionOptions", Convert = "{0} is not null ? {0}.OperationName : null")] string? operationName,
        [QylFromArgument(0, Type = "GraphQL.ExecutionOptions", Convert = "{0} is not null ? {0}.Query : null")] string? document)
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
}
