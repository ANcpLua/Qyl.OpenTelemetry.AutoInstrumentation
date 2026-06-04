using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedGraphQl
{
    private const string GraphQlDomain = "graphql";

    public static Activity? StartActivity(string? document, string? operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GraphQl))
            return null;

        var normalizedOperationName = string.IsNullOrWhiteSpace(operationName) ? "ExecuteAsync" : operationName;
        var activity = QylActivitySource.Source.StartActivity("GraphQL " + normalizedOperationName, ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, GraphQlDomain);
        activity.SetTag(QylSemanticAttributes.GraphQlOperationName, normalizedOperationName);

        if (QylAutoInstrumentationOptions.Current.GraphQlSetDocument && !string.IsNullOrWhiteSpace(document))
            activity.SetTag(QylSemanticAttributes.GraphQlDocument, document);

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
}
