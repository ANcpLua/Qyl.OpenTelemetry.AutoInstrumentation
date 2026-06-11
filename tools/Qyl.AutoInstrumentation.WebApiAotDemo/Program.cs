using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qyl.AutoInstrumentation;
using Qyl.RealEfCoreDemo;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

using var downstream = new HttpClient(new StubHandler())
{
    BaseAddress = new Uri("https://qyl-webapi.invalid"),
};
using (await downstream.GetAsync("/downstream?secret=redacted"))
{
}

await using (var sqlConnection = new SqlConnection())
{
    await using var sqlCommand = sqlConnection.CreateCommand();
    sqlCommand.CommandText = "SELECT 1";
    try
    {
        _ = await sqlCommand.ExecuteScalarAsync();
    }
    catch (InvalidOperationException)
    {
    }
}

await using var sqlite = new SqliteConnection("Data Source=:memory:");
await sqlite.OpenAsync();
await CreateSchemaAsync(sqlite);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.WebHost.SuppressStatusMessages(true);
builder.Logging.ClearProviders();
var app = builder.Build();

app.MapGet("/probe/{id:int}", async () =>
{
    await using (var db = new ProbeContext(sqlite))
    {
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Items (Name) VALUES ('webapi')");
    }

    return Results.NoContent();
});

await app.StartAsync();

try
{
    var address = app.Urls.Single();
    using var client = new HttpClient();
    using (await client.GetAsync(address + "/probe/42?secret=redacted"))
    {
    }
}
finally
{
    await app.StopAsync();
}

var report = WebApiAotReport.Create(captured.ToArray());
var json = JsonSerializer.Serialize(report, WebApiAotJsonContext.Default.WebApiAotReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task CreateSchemaAsync(SqliteConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE Items (
            Id INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL
        );
        """;
    await command.ExecuteNonQueryAsync();
}

internal sealed class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            RequestMessage = request,
        });
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

internal sealed record MatchedSignal(
    string Signal,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags);

internal sealed record WebApiAotReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    MatchedSignal[] Signals,
    MatchedSignal[] Activities)
{
    public static WebApiAotReport Create(CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var signals = new List<MatchedSignal>();

        AddRequired(signals, failures, "aspnetcore.server", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.server") &&
            HasTag(activity, "http.route", "/probe/{id:int}")));

        AddRequired(signals, failures, "httpclient.self", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "server.address", "127.0.0.1")));

        AddRequired(signals, failures, "httpclient.downstream", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "http.request.method", "GET") &&
            HasTag(activity, "http.response.status_code", "204") &&
            HasTag(activity, "server.address", "qyl-webapi.invalid")));

        AddRequired(signals, failures, "efcore.sqlite", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.efcore")));

        AddRequired(signals, failures, "sqlclient.command", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.sqlclient") &&
            HasTag(activity, "db.operation.name", "SELECT") &&
            HasTag(activity, "error.type", "System.InvalidOperationException")));

        foreach (var signal in signals)
        {
            if (signal.Tags.ContainsKey("url.full") ||
                signal.Tags.ContainsKey("url.path") ||
                signal.Tags.ContainsKey("db.query.text"))
            {
                failures.Add("sensitive raw value leaked in " + signal.Signal);
            }
        }

        return new WebApiAotReport(
            RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
            failures.Count is 0,
            failures.ToArray(),
            signals.OrderBy(static signal => signal.Signal, StringComparer.Ordinal).ToArray(),
            activities
                .Select(static activity => new MatchedSignal("activity", activity.Name, activity.Kind, activity.Status, Canonicalize(activity.Tags)))
                .OrderBy(static signal => signal.Name, StringComparer.Ordinal)
                .ThenBy(static signal => signal.Kind, StringComparer.Ordinal)
                .ThenBy(static signal => string.Join(",", signal.Tags.Select(static pair => pair.Key + "=" + pair.Value)), StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddRequired(
        ICollection<MatchedSignal> signals,
        ICollection<string> failures,
        string signal,
        CapturedActivity? activity)
    {
        if (activity is null)
        {
            failures.Add("missing " + signal);
            return;
        }

        signals.Add(new MatchedSignal(signal, activity.Name, activity.Kind, activity.Status, Canonicalize(activity.Tags)));
    }

    private static bool HasTag(CapturedActivity activity, string key, string expected)
        => activity.Tags.TryGetValue(key, out var actual) &&
           StringComparer.Ordinal.Equals(actual, expected);

    private static IReadOnlyDictionary<string, string> Canonicalize(IReadOnlyDictionary<string, string> tags)
    {
        var keep = new[]
        {
            "qyl.instrumentation.domain",
            "http.request.method",
            "http.route",
            "http.response.status_code",
            "server.address",
            "server.port",
            "db.system",
            "db.operation.name",
            "db.query.summary",
            "error.type",
        };
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keep)
        {
            if (!tags.TryGetValue(key, out var value))
                continue;

            result[key] = key is "server.port" ? "<port>" : value;
        }

        return result;
    }
}

[JsonSerializable(typeof(WebApiAotReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class WebApiAotJsonContext : JsonSerializerContext;
