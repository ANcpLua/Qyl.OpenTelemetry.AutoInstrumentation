using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>MongoDB collection operation spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.MongoDb, QylAttributes.InstrumentationDomainValues.DbMongoDb)]
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
            QylSpanNames.Db(summary, DbIncubatingAttributes.SystemNameValues.Mongodb),
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.DbMongoDb);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            DbIncubatingAttributes.SystemNameValues.Mongodb,
            operation,
            summary);
        if (!string.IsNullOrEmpty(collectionName))
            activity.SetTag(DbIncubatingAttributes.CollectionName, collectionName);
        if (!string.IsNullOrEmpty(databaseName))
            activity.SetTag(DbAttributes.Namespace, databaseName);

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
