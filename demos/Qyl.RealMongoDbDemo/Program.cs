using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MongoDB.Bson;
using MongoDB.Driver;
using Qyl.AutoInstrumentation;

var connectionString = Environment.GetEnvironmentVariable("QYL_MONGODB_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_MONGODB_CONNECTION_STRING is required.");
    return 2;
}

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var client = new MongoClient(connectionString);
await WaitForMongoDbAsync(client);

var database = client.GetDatabase("qyl");
await database.DropCollectionAsync("probe");
var collection = database.GetCollection<BsonDocument>("probe");

await collection.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "alpha" } });

var count = await collection.CountDocumentsAsync(Builders<BsonDocument>.Filter.Eq("_id", 1));
Console.WriteLine("counted-documents=" + count.ToString(CultureInfo.InvariantCulture));

try
{
    await collection.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "duplicate" } });
}
catch (MongoWriteException exception)
{
    Console.WriteLine("expected-mongodb-error=" + exception.WriteError.Category);
}

var deleted = await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
Console.WriteLine("deleted-documents=" + deleted.DeletedCount.ToString(CultureInfo.InvariantCulture));

var report = MongoDbReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealMongoDbJsonContext.Default.MongoDbReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForMongoDbAsync(MongoClient client)
{
    Exception? lastException = null;

    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            using var names = await client.ListDatabaseNamesAsync();
            await names.MoveNextAsync();
            return;
        }
        catch (MongoException exception)
        {
            lastException = exception;
        }
        catch (TimeoutException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("MongoDB did not become ready.", lastException);
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record MongoDbReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static MongoDbReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var mongoSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.DbMongoDb))
            .ToArray();

        if (mongoSpans.Length != 4)
            failures.Add($"expected 4 MongoDB command spans, got {mongoSpans.Length}");

        var insertSuccess = FindByOperationAndStatus(mongoSpans, "insert", "Unset");
        var insertError = FindByOperationAndStatus(mongoSpans, "insert", "Error");
        var countSuccess = FindByOperationAndStatus(mongoSpans, "count", "Unset");
        var deleteSuccess = FindByOperationAndStatus(mongoSpans, "delete", "Unset");

        Require(insertSuccess, "successful insert span", failures);
        Require(insertError, "error insert span", failures);
        Require(countSuccess, "successful count span", failures);
        Require(deleteSuccess, "successful delete span", failures);
        RequireTag(insertError, QylSemanticAttributes.ErrorType, "MongoWriteException", failures);

        foreach (var span in mongoSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "MongoDB command"))
                failures.Add($"unexpected MongoDB span name: {span.Name}");

            RequireTag(span, QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemMongodb, failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");
        }

        return new MongoDbReport(runtimeMode, failures.Count is 0, failures.ToArray(), mongoSpans);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(QylSemanticAttributes.DbOperationName, out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(MongoDbReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMongoDbJsonContext : JsonSerializerContext;
