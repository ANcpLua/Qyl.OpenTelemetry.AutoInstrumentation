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

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
