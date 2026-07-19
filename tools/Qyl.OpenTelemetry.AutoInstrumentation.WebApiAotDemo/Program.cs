using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.RealEfCoreDemo;

var captured = new List<CapturedActivity>();
var capturedLock = new Lock();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            captured.Add(CapturedActivity.From(activity));
        }
    },
};

ActivitySource.AddActivityListener(listener);

await using var downstreamServer = LoopbackHttpServer.Start();
using var downstream = new HttpClient();
using (await downstream.GetAsync(downstreamServer.Uri + "downstream?secret=redacted"))
{
}
await downstreamServer.RequestCompleted;

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

// CreateSlimBuilder is the NativeAOT-recommended entry point: it wires only the minimal
// ASP.NET Core feature set (no IIS, HTTPS, static-web-assets, or hosting-startup), which keeps
// the published native image lean. This demo is HTTP-only minimal-API, so the slim builder is a
// drop-in. Inline `:int` route constraints stay supported (only regex/alpha constraints are not).
var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.WebHost.SuppressStatusMessages(true);
builder.Logging.ClearProviders();
builder.Services.AddQylAspNetCoreInstrumentation();
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

var report = WebApiAotReport.Create(captured.ToArray(), downstreamServer.Port);
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

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private LoopbackHttpServer(TcpListener listener)
    {
        _listener = listener;
        Port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Uri = new Uri($"http://127.0.0.1:{Port}/", UriKind.Absolute);
        RequestCompleted = ServeOnceAsync(listener);
    }

    public int Port { get; }

    public Uri Uri { get; }

    public Task RequestCompleted { get; }

    public static LoopbackHttpServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        return new LoopbackHttpServer(listener);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        var received = new List<byte>();
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                throw new InvalidOperationException("Loopback client closed before sending HTTP headers.");
            received.AddRange(buffer.AsSpan(0, count).ToArray());
            if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    }
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
    public static WebApiAotReport Create(CapturedActivity[] activities, int downstreamPort)
    {
        var failures = new List<string>();
        var signals = new List<MatchedSignal>();

        // Server span is owned by the explicit middleware; the DiagnosticListener observes only the
        // ambient start in this mode, so exactly one server span is emitted and
        // it carries the aspnetcore.server domain (with the route backfilled after routing).
        AddRequired(signals, failures, "aspnetcore.server", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "aspnetcore.server") &&
            HasTag(activity, "http.route", "/probe/{id:int}")));

        AddRequired(signals, failures, "httpclient.self", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "server.address", "127.0.0.1")));

        AddRequired(signals, failures, "httpclient.downstream", activities.FirstOrDefault(activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "http.request.method", "GET") &&
            HasTag(activity, "http.response.status_code", "204") &&
            HasTag(activity, "server.address", "127.0.0.1") &&
            activity.Tags.TryGetValue("server.port", out var port) &&
            int.TryParse(port, CultureInfo.InvariantCulture, out var parsedPort) &&
            parsedPort == downstreamPort));

        AddRequired(signals, failures, "efcore.sqlite", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.efcore")));

        AddRequired(signals, failures, "sqlclient.command", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.client") &&
            HasTag(activity, "db.operation.name", "SELECT") &&
            HasTag(activity, "error.type", "System.InvalidOperationException")));

        foreach (var signal in signals)
        {
            if (signal.Tags.ContainsKey("db.query.text"))
                failures.Add("db.query.text leaked with default options in " + signal.Signal);
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
            "url.scheme",
            "http.response.status_code",
            "server.address",
            "server.port",
            "db.system.name",
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
