using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedGraphQl
{
    private const string GraphQlDomain = "graphql";

    public static Activity? StartActivity()
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GraphQl))
            return null;

        var activity = QylActivitySource.Source.StartActivity("GraphQL ExecuteAsync", ActivityKind.Internal);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, GraphQlDomain);
        activity.SetTag(QylSemanticAttributes.GraphQlOperationName, "ExecuteAsync");
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
