using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Qyl;
using Qyl.Telemetry.TestFixtures;

var scenario = ClientCaptureScenario.Parse(args);
var profile = scenario.Profile;
Require(!RuntimeFeature.IsDynamicCodeSupported, "the fixture must execute as NativeAOT");
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
var serverBuilder = WebApplication.CreateSlimBuilder();
serverBuilder.Logging.ClearProviders();
serverBuilder.WebHost.ConfigureKestrel(static options => options.Listen(IPAddress.Loopback, 0));
await using var server = serverBuilder.Build();
server.Run(static async context =>
{
    if (context.Request.Path == "/fail") { context.Abort(); return; }
    if (context.Request.Path == "/empty") { context.Response.StatusCode = 204; return; }
    context.Response.ContentType = "application/result";
    context.Response.Headers["X-RateLimit-Remaining"] = "7";
    context.Response.Headers["X-Custom-Multi"] = new[] { "response-a", "response-b" };
    await context.Response.WriteAsync("ok", context.RequestAborted);
});
await server.StartAsync(timeout.Token);

var exporter = new SnapshotExporter();
using var http = new HttpClient();
http.BaseAddress = new Uri(server.Urls.Single());
var builder = Host.CreateApplicationBuilder();
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.AddQyl(options =>
{
    options.EnableCollectorDiscovery = false;
    options.RequireConfiguredEndpoint = true;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.AdditionalSources.Add("HeaderCapture.Fixture");
    options.ConfigureTracing = tracing =>
    {
        if (scenario.Control is ClientCaptureControl.Unsampled) tracing.SetSampler(new AlwaysOffSampler());
        tracing.AddProcessor(new BatchActivityExportProcessor(exporter));
    };
});
using (var host = builder.Build())
{
    await host.StartAsync(timeout.Token);
    using var source = new ActivitySource("HeaderCapture.Fixture");
    using var parent = source.CreateActivity("capture-parent", ActivityKind.Internal)?.Start();
    using (SuppressInstrumentationScope.Begin(scenario.Control is ClientCaptureControl.Suppressed))
    {
        using var request = Request("/ok", true);
        using var response = await http.SendAsync(request, timeout.Token);
        Require(response.StatusCode == HttpStatusCode.OK, "successful POST changed");
        using var empty = Request("/empty", false);
        using var emptyResponse = await http.SendAsync(empty, timeout.Token);
        Require(emptyResponse.StatusCode == HttpStatusCode.NoContent, "content-free request changed");
        using var failed = Request("/fail", true);
        try
        {
            using (await http.SendAsync(failed, timeout.Token))
                throw new InvalidOperationException("transport failure unexpectedly succeeded");
        }
        catch (HttpRequestException exception)
        {
            Require(exception.StatusCode is null, "transport abort unexpectedly returned an HTTP status");
        }
    }
    Require(parent?.TagObjects.Any(static tag => tag.Key.StartsWith("http.request.header.", StringComparison.Ordinal) ||
        tag.Key.StartsWith("http.response.header.", StringComparison.Ordinal)) != true, "unrelated parent was decorated");
    Require(host.Services.GetRequiredService<TracerProvider>().ForceFlush(), "flush failed");
    var active = scenario.EmitsSpans;
    var spans = exporter.Spans.ToArray();
    string[] expectedPaths = active ? ["/empty", "/fail", "/ok"] : [];
    var actualPaths = spans.Select(static span => new Uri((string)span.Tags["url.full"]!).AbsolutePath)
        .Order(StringComparer.Ordinal).ToArray();
    Require(actualPaths.SequenceEqual(expectedPaths),
        $"native HTTP operations changed: expected [{string.Join(", ", expectedPaths)}], actual [{string.Join(", ", actualPaths)}]");
    foreach (var span in spans)
    {
        Require(span is { Source: "System.Net.Http", Operation: "System.Net.Http.HttpRequestOut", Kind: ActivityKind.Client }, "native span ownership changed");
        var path = new Uri((string)span.Tags["url.full"]!).AbsolutePath;
        var requestCaptured = scenario.RequestCaptured;
        var responseCaptured = scenario.ResponseCaptured;
        if (requestCaptured)
        {
            Values(span, "http.request.header.x-tenant-id", ["tenant"]);
            Values(span, "http.request.header.x-request-id", ["dash"]);
            Values(span, "http.request.header.x_request_id", ["underscore"]);
            Values(span, "http.request.header.x.request.id", ["dot"]);
            Values(span, "http.request.header.x-custom-multi", ["first", "first", "", "comma,kept"]);
            if (path != "/empty")
            {
                Values(span, "http.request.header.content-type", ["application/custom"]);
                Values(span, "http.request.header.content-disposition", ["attachment; filename=sample.txt"]);
            }
            else Require(!span.Tags.ContainsKey("http.request.header.content-type"), "invented request content header");
        }
        else Require(!span.Tags.Keys.Any(static key => key.StartsWith("http.request.header.", StringComparison.Ordinal)), "request capture was not opt-in");
        if (responseCaptured && path == "/ok")
        {
            Values(span, "http.response.header.x-ratelimit-remaining", ["7"]);
            Values(span, "http.response.header.content-type", ["application/result"]);
            Values(span, "http.response.header.x-custom-multi", ["response-a", "response-b"]);
        }
        else Require(!span.Tags.Keys.Any(static key => key.StartsWith("http.response.header.", StringComparison.Ordinal)), "invented or unselected response header");
        Require(!span.Tags.ContainsKey("http.request.header.unselected"), "unselected header captured");
        Require(span.Status == (path == "/fail" ? ActivityStatusCode.Error : ActivityStatusCode.Unset), "native status changed");
        if (parent is not null) Require(span.Parent == parent.SpanId, "native parentage changed");
    }
    await host.StopAsync(timeout.Token);
}

// Provider disposal must remove the capture subscription even if another listener keeps native spans enabled.
var afterDispose = new List<Dictionary<string, object?>>();
using (var observer = new ActivityListener())
{
    observer.ShouldListenTo = static source => source.Name == "System.Net.Http";
    observer.Sample = static (ref _) => ActivitySamplingResult.AllDataAndRecorded;
    observer.ActivityStopped = activity => afterDispose.Add(activity.TagObjects.ToDictionary(static tag => tag.Key, static tag => tag.Value));
    ActivitySource.AddActivityListener(observer);
    using var request = Request("/empty", false);
    using var response = await http.SendAsync(request, timeout.Token);
}
Require(afterDispose.Count == 1 && !afterDispose[0].Keys.Any(static key => key.StartsWith("http.request.header.", StringComparison.Ordinal) ||
    key.StartsWith("http.response.header.", StringComparison.Ordinal)), "capture subscription outlived its provider");
await server.StopAsync(timeout.Token);
exporter.Report(profile);

static HttpRequestMessage Request(string path, bool content)
{
    var request = new HttpRequestMessage(content ? HttpMethod.Post : HttpMethod.Get, path);
    request.Headers.TryAddWithoutValidation("X-Tenant-Id", "tenant");
    request.Headers.TryAddWithoutValidation("X-Request-Id", "dash");
    request.Headers.TryAddWithoutValidation("X_Request_Id", "underscore");
    request.Headers.TryAddWithoutValidation("X.Request.Id", "dot");
    request.Headers.TryAddWithoutValidation("X-Custom-Multi", ["first", "first", "", "comma,kept"]);
    request.Headers.TryAddWithoutValidation("Unselected", "absent");
    if (content)
    {
        request.Content = new StringContent("payload");
        request.Content.Headers.ContentType = new MediaTypeHeaderValue("application/custom");
        request.Content.Headers.TryAddWithoutValidation("Content-Disposition", "attachment; filename=sample.txt");
    }
    return request;
}

static void Values(Snapshot span, string key, string[] expected)
{
    Require(span.Tags.TryGetValue(key, out var value) && value is string[] actual && actual.SequenceEqual(expected),
        "missing, non-array or changed values: " + key);
}

static void Require(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

sealed record Snapshot(string Source, string Operation, ActivityKind Kind, ActivityStatusCode Status,
    ActivitySpanId Parent, Dictionary<string, object?> Tags);

sealed class SnapshotExporter : BaseExporter<Activity>
{
    internal ConcurrentQueue<Snapshot> Spans { get; } = new();

    public override ExportResult Export(in Batch<Activity> batch)
    {
        foreach (var activity in batch)
        {
            if (activity.Source.Name != "System.Net.Http") continue;
            Spans.Enqueue(new(activity.Source.Name, activity.OperationName, activity.Kind, activity.Status,
                activity.ParentSpanId, activity.TagObjects.ToDictionary(static tag => tag.Key,
                    static tag => tag.Value is string[] values ? values.ToArray() : tag.Value)));
        }
        return ExportResult.Success;
    }

    internal void Report(string profile)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            writer.WriteString("status", "passed");
            writer.WriteString("profile", profile);
            writer.WriteString("runtime", RuntimeInformation.FrameworkDescription);
            writer.WriteBoolean("dynamicCodeSupported", RuntimeFeature.IsDynamicCodeSupported);
            writer.WriteNumber("native_span_count", Spans.Count);
            writer.WriteStartArray("spans");
            foreach (var span in Spans)
            {
                writer.WriteStartObject();
                writer.WriteString("source", span.Source);
                writer.WriteString("operation", span.Operation);
                writer.WriteString("kind", span.Kind.ToString());
                writer.WriteString("status", span.Status.ToString());
                writer.WriteStartObject("captured");
                foreach (var (key, value) in span.Tags)
                {
                    if (!key.StartsWith("http.request.header.", StringComparison.Ordinal) && !key.StartsWith("http.response.header.", StringComparison.Ordinal)) continue;
                    if (value is not string[] values) throw new InvalidOperationException("exported field is not string[]");
                    writer.WriteStartArray(key);
                    foreach (var item in values) writer.WriteStringValue(item);
                    writer.WriteEndArray();
                }
                writer.WriteEndObject();
                writer.WriteEndObject();
            }
            writer.WriteEndArray();
            writer.WriteEndObject();
        }
        Console.WriteLine(Encoding.UTF8.GetString(stream.ToArray()));
    }
}
