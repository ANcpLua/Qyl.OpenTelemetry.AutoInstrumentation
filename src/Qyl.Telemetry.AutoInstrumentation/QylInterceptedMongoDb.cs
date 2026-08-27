using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>MongoDB collection operation spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.MongoDb, QylInstrumentationDomains.DbMongoDb)]
[QylIntercept(
    "MongoDB.Driver.IMongoCollection`1",
    "Find", "FindAsync",
    "Aggregate", "AggregateAsync",
    "InsertOne", "InsertOneAsync", "InsertMany", "InsertManyAsync",
    "ReplaceOne", "ReplaceOneAsync",
    "DeleteOne", "DeleteOneAsync", "DeleteMany", "DeleteManyAsync",
    "UpdateOne", "UpdateOneAsync", "UpdateMany", "UpdateManyAsync",
    "CountDocuments", "CountDocumentsAsync", "EstimatedDocumentCount", "EstimatedDocumentCountAsync",
    Shape = QylShapes.MongoDbCollection,
    Start = nameof(Operation),
    ObserveAsync = true)]
public static class QylInterceptedMongoDb
{
    /// <summary>Starts the client span named <c>{operation} {collection}</c>.</summary>
    public static Activity? Operation(
        [QylFromMethodName] string operationName,
        [QylFromReceiver("CollectionNamespace?.CollectionName")] string? collectionName,
        [QylFromReceiver("Database?.DatabaseNamespace?.DatabaseName")] string? databaseName)
    {
        ArgumentNullException.ThrowIfNull(operationName);

        var operation = NormalizeOperation(operationName);
        var summary = string.IsNullOrEmpty(collectionName) ? operation : operation + " " + collectionName;
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.MongoDb,
            QylSpanNames.Db(summary, QylSemanticAttributes.DbSystemMongodb),
            ActivityKind.Client,
            QylInstrumentationDomains.DbMongoDb);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            QylSemanticAttributes.DbSystemMongodb,
            operation,
            summary);
        if (!string.IsNullOrEmpty(collectionName))
            activity.SetTag(QylSemanticAttributes.DbCollectionName, collectionName);
        if (!string.IsNullOrEmpty(databaseName))
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);

        return activity;
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
