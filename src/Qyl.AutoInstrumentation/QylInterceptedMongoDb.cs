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
        activity.SetTag(QylSemanticAttributes.DbSystemName, "mongodb");
        activity.SetTag(QylSemanticAttributes.DbOperationName, operation);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, operation);
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
