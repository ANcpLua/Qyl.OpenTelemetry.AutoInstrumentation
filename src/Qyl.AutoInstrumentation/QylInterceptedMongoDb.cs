using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Mongo Db.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedMongoDb);</code></example>
public static class QylInterceptedMongoDb
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string operationName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        var operation = NormalizeOperation(operationName);
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.MongoDb,
            QylActivityNames.MongoDbCommand,
            ActivityKind.Client,
            QylInstrumentationDomains.DbMongoDb);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            QylSemanticAttributes.DbSystemMongodb,
            operation,
            operation);
        return activity;
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Observe Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task ObserveAsync(Task? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Observes an asynchronous operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
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
