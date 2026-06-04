using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedMongoDb
{
    private const string MongoDbDomain = "db.mongodb";

    public static Activity? StartActivity(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.MongoDb))
            return null;

        var operation = NormalizeOperation(operationName);
        var activity = QylActivitySource.Source.StartActivity("MongoDB " + operation, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, MongoDbDomain);
        activity.SetTag(QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemMongodb);
        activity.SetTag(QylSemanticAttributes.DbOperationName, operation);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, operation);
        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static Task ObserveAsync(Task? task, Activity? activity)
    {
        if (activity is null || task is null)
        {
            activity?.Dispose();
            return task!;
        }

        return ObserveSlowAsync(task, activity);
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

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static string NormalizeOperation(string operationName)
        => operationName switch
        {
            "FindAsync" => "find",
            "AggregateAsync" => "aggregate",
            "InsertOneAsync" or "InsertManyAsync" => "insert",
            "ReplaceOneAsync" => "replace",
            "DeleteOneAsync" or "DeleteManyAsync" => "delete",
            "UpdateOneAsync" or "UpdateManyAsync" => "update",
            "CountDocumentsAsync" or "EstimatedDocumentCountAsync" => "count",
            _ => operationName,
        };
}
