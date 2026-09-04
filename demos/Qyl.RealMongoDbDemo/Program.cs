using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using MongoDB.Bson;
using MongoDB.Driver;
using OpenTelemetry.Trace;
using Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

var connectionString = Environment.GetEnvironmentVariable("QYL_MONGODB_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_MONGODB_CONNECTION_STRING is required.");
    return 2;
}

// The real registration path: AddQyl subscribes to MongoDB.Driver's own ActivitySource and installs
// the one native-span processor.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealMongoDbDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-mongodb-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

var client = new MongoClient(connectionString);
await WaitForMongoDbAsync(client);

// The container is fresh for every run, so the collection is never dropped first: a drop would be a
// fourth command naming the collection, and the assertion is one span per command.
var database = client.GetDatabase(MongoDbReport.DatabaseName);
var collection = database.GetCollection<BsonDocument>(MongoDbReport.CollectionName);

await collection.InsertOneAsync(new BsonDocument { { "_id", 1 }, { "name", "alpha" } });
Console.WriteLine("inserted-documents=1");

using (var cursor = await collection.FindAsync(Builders<BsonDocument>.Filter.Eq("_id", 1)))
{
    var found = await cursor.ToListAsync();
    Console.WriteLine("found-documents=" + found.Count.ToString(CultureInfo.InvariantCulture));
}

var deleted = await collection.DeleteManyAsync(Builders<BsonDocument>.Filter.Empty);
Console.WriteLine("deleted-documents=" + deleted.DeletedCount.ToString(CultureInfo.InvariantCulture));

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = MongoDbReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

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
    string Source,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
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
    internal const string DatabaseName = "qyl";
    internal const string CollectionName = "probe";

    public static MongoDbReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();

        // The driver's source also carries the commands it issues on its own -- the handshake and the
        // topology checks. The probe's own commands are the ones naming the collection, and each of
        // them must appear exactly once.
        var mongoSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.MongoDBDriver))
            .Where(static activity =>
                activity.Tags.TryGetValue(DbIncubatingAttributes.CollectionName, out var collection) &&
                StringComparer.Ordinal.Equals(collection, CollectionName))
            .ToArray();

        // The driver traces each call in two layers: an operation span carrying db.operation.name,
        // and the command span beneath it carrying db.command.name and the wire details. Each layer
        // must appear exactly once per command.
        foreach (var command in new[] { "insert", "find", "delete" })
        {
            RequireExactlyOne(mongoSpans, DbAttributes.OperationName, command, "operation", failures);
            RequireExactlyOne(mongoSpans, DbIncubatingAttributes.CommandName, command, "command", failures);
        }

        foreach (var span in mongoSpans)
        {
            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.DbMongoDb,
                failures);

            // The driver's own attributes, which are the stable database conventions. Only the
            // operation layer names the operation; the command layer names the wire command.
            RequireTag(span, DbAttributes.SystemName, DbIncubatingAttributes.SystemNameValues.Mongodb, failures);
            RequireTag(span, DbAttributes.Namespace, DatabaseName, failures);
            if (!span.Tags.ContainsKey(DbAttributes.OperationName) &&
                !span.Tags.ContainsKey(DbIncubatingAttributes.CommandName))
                failures.Add($"span {span.Name} names neither an operation nor a command");

            // The driver adds db.query.text only when the consumer raises
            // MongoClientSettings.TracingOptions.QueryTextMaxLength, which defaults to 0. qyl does
            // not raise it, so the command text never leaves the process.
            RequireMissingTag(span, DbAttributes.QueryText, failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");
        }

        return new MongoDbReport(runtimeMode, failures.Count is 0, failures.ToArray(), mongoSpans);
    }

    private static void RequireExactlyOne(
        CapturedActivity[] spans,
        string key,
        string expected,
        string layer,
        ICollection<string> failures)
    {
        var matching = spans
            .Where(span => span.Tags.TryGetValue(key, out var actual) && StringComparer.Ordinal.Equals(actual, expected))
            .ToArray();
        if (matching.Length != 1)
            failures.Add($"expected exactly 1 MongoDB '{expected}' {layer} span, got {matching.Length.ToString(CultureInfo.InvariantCulture)}");
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key}");
    }

    private static void RequireMissingTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (span.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(MongoDbReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMongoDbJsonContext : JsonSerializerContext;
