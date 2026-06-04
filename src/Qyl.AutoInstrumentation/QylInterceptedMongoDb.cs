using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedMongoDb
{

    public static Activity? StartActivity(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.MongoDb))
            return null;

        var operation = NormalizeOperation(operationName);
        var activity = QylActivitySource.Source.StartActivity("MongoDB command", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbMongoDb);
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
            "Find" or "FindAsync" => "find",
            "Aggregate" or "AggregateAsync" => "aggregate",
            "InsertOne" or "InsertOneAsync" or "InsertMany" or "InsertManyAsync" => "insert",
            "ReplaceOne" or "ReplaceOneAsync" => "replace",
            "DeleteOne" or "DeleteOneAsync" or "DeleteMany" or "DeleteManyAsync" => "delete",
            "UpdateOne" or "UpdateOneAsync" or "UpdateMany" or "UpdateManyAsync" => "update",
            "CountDocuments" or "CountDocumentsAsync" or "EstimatedDocumentCount" or "EstimatedDocumentCountAsync" => "count",
            _ => operationName,
        };
}
