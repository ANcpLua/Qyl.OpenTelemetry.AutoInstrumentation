using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

// The real registration path: AddQyl subscribes to the BCL's own System.Net.Http ActivitySource and
// installs the one native-span processor. qyl writes no HTTP client span of its own -- the whole
// convention is the BCL's.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealHttpClientDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-http-client-demo";
    // The live-check gate points this at its OTLP listener. Unset, the demo exports into a
    // closed port and asserts on its in-memory exporter alone.
    options.CollectorEndpoint =
        new Uri(Environment.GetEnvironmentVariable("QYL_LIVE_CHECK_ENDPOINT") ?? "http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;

    // A consumer naming a source qyl already subscribes -- "System.Net.Http" is the one they reach
    // for. AddQyl deduplicates AdditionalSources against the sources it registered itself, so the
    // source is not subscribed twice and each request still produces exactly one span. The
    // one-span-per-request assertion below is the executable proof that it does not double
    // subscribe.
    options.AdditionalSources.Add(HttpClientTelemetry.SourceName);
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

var capturedSpans = new List<CapturedSpan>();
var capturedLock = new Lock();
var capturedMetrics = new List<CapturedMetric>();
var operation = HttpClientReport.ErrorStatusOperation;

// Listen to EVERY ActivitySource in the process, not only the ones qyl subscribes, so the report
// can assert the EXACT set of (scope, kind) tuples each operation produces rather than a filtered
// subset. Sources whose name starts with "Experimental." are the single exclusion: they are the
// runtime's own socket / DNS / TLS / connection-pool diagnostics, which qyl never subscribes and
// which appear here only because this listener listens to everything. That prefix is the ONLY
// exclusion -- any other third source shows up in the asserted multiset and fails the gate.
using var listener = new ActivityListener
{
    ShouldListenTo = static _ => true,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            capturedSpans.Add(new CapturedSpan(operation, activity.Source.Name, activity.Kind.ToString()));
        }
    },
};

ActivitySource.AddActivityListener(listener);

using var meterListener = new MeterListener
{
    InstrumentPublished = static (instrument, listener) =>
    {
        if (StringComparer.Ordinal.Equals(instrument.Meter.Name, HttpClientTelemetry.MeterName) &&
            StringComparer.Ordinal.Equals(instrument.Name, HttpClientTelemetry.RequestDurationInstrument))
        {
            listener.EnableMeasurementEvents(instrument);
        }
    },
};

meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
    capturedMetrics.Add(CapturedMetric.From(instrument, measurement, tags)));
meterListener.Start();

using var httpClient = new HttpClient();

// One request that returns a real HTTP error status, so error.type carries the status code.
var server = StartOneShotHttpServer(503);
using (var response = await httpClient.GetAsync($"http://127.0.0.1:{server.Port}/probe?token=secret"))
{
    Console.WriteLine("error-status=" + ((int)response.StatusCode).ToString(CultureInfo.InvariantCulture));
}

await server.Completion;

// One request to a dead port, so error.type carries connection_error and no status code exists.
operation = HttpClientReport.ConnectionFailureOperation;
try
{
    using var response = await httpClient.GetAsync("http://127.0.0.1:1/fail?token=secret");
}
catch (HttpRequestException exception)
{
    Console.WriteLine($"expected-failure={exception.GetType().Name}");
}

// Stop listening before the SDK flushes. The OTLP exporter's own calls to the collector are
// System.Net.Http requests too, and they belong to none of the asserted operations.
listener.Dispose();
meterListener.Dispose();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = HttpClientReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    server.Port,
    capturedSpans.ToArray(),
    exportedActivities.Select(CapturedActivity.From).ToArray(),
    capturedMetrics.ToArray());

var json = JsonSerializer.Serialize(report, RealHttpClientJsonContext.Default.HttpClientReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static OneShotHttpServer StartOneShotHttpServer(int statusCode)
{
    var server = new TcpListener(IPAddress.Loopback, 0);
    server.Start();
    var port = ((IPEndPoint)server.LocalEndpoint).Port;

    var completion = Task.Run(async () =>
    {
        try
        {
            using var client = await server.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            using var reader = new StreamReader(stream, leaveOpen: true);

            while (!string.IsNullOrEmpty(await reader.ReadLineAsync()))
            {
            }

            var response = $"HTTP/1.1 {statusCode} Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n";
            await stream.WriteAsync(Encoding.ASCII.GetBytes(response));
        }
        finally
        {
            server.Stop();
        }
    });

    return new OneShotHttpServer(port, completion);
}

internal static class HttpClientTelemetry
{
    // The BCL owns both: one ActivitySource and one Meter, both named System.Net.Http.
    internal const string SourceName = "System.Net.Http";
    internal const string MeterName = "System.Net.Http";
    internal const string RequestDurationInstrument = "http.client.request.duration";
}

internal sealed record OneShotHttpServer(int Port, Task Completion);

/// <summary>One <c>(scope, kind)</c> tuple the raw listener saw, tagged with the operation it fell in.</summary>
internal sealed record CapturedSpan(string Operation, string Scope, string Kind)
{
    public string Tuple => Scope + "/" + Kind;
}

internal sealed record OperationSpanSet(string Operation, string[] Expected, string[] Actual);

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string OperationName,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.OperationName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record CapturedMetric(
    string MeterName,
    string Name,
    double Value,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedMetric From(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var capturedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
            capturedTags[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;

        return new CapturedMetric(instrument.Meter.Name, instrument.Name, value, capturedTags);
    }
}

internal sealed record HttpClientReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    OperationSpanSet[] SpanSets,
    string[] ExcludedScopes,
    CapturedActivity[] Activities,
    CapturedMetric[] Metrics)
{
    internal const string ErrorStatusOperation = "error-status";
    internal const string ConnectionFailureOperation = "connection-failure";

    // The runtime's own socket / DNS / TLS / connection-pool diagnostics. This is the ONE prefix the
    // span-set assertion excludes; everything else the process emits has to be an expected tuple.
    private const string ExcludedScopePrefix = "Experimental.";

    private const string QylScope = "Qyl.Telemetry.AutoInstrumentation";

    public static HttpClientReport Create(
        string runtimeMode,
        int serverPort,
        CapturedSpan[] capturedSpans,
        CapturedActivity[] activities,
        CapturedMetric[] metrics)
    {
        var failures = new List<string>();

        var excludedScopes = capturedSpans
            .Where(static span => span.Scope.StartsWith(ExcludedScopePrefix, StringComparison.Ordinal))
            .Select(static span => span.Scope)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // One HttpClient.GetAsync produces exactly one non-Experimental span: the BCL's own
        // System.Net.Http client span. The deleted qyl listener span reappearing, or any other
        // library joining in, changes this multiset and fails the gate.
        var spanSets = new[]
        {
            SpanSet(capturedSpans, ErrorStatusOperation, [HttpClientTelemetry.SourceName + "/Client"], failures),
            SpanSet(capturedSpans, ConnectionFailureOperation, [HttpClientTelemetry.SourceName + "/Client"], failures),
        };

        // Stated separately from the multiset so the deleted qyl span is named in the failure.
        var qylScopeSpans = capturedSpans.Count(static span => StringComparer.Ordinal.Equals(span.Scope, QylScope));
        if (qylScopeSpans is not 0)
            failures.Add($"expected 0 spans from the {QylScope} scope, got {qylScopeSpans.ToString(CultureInfo.InvariantCulture)}");

        var httpSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(activity.Source, HttpClientTelemetry.SourceName))
            .ToArray();

        if (httpSpans.Length != 2)
            failures.Add($"expected 2 exported System.Net.Http spans, got {httpSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        var statusSpan = httpSpans.FirstOrDefault(static activity =>
            activity.Tags.ContainsKey(HttpAttributes.ResponseStatusCode));
        var failureSpan = httpSpans.FirstOrDefault(static activity =>
            !activity.Tags.ContainsKey(HttpAttributes.ResponseStatusCode));

        Require(statusSpan, "503 status span", failures);
        Require(failureSpan, "connection failure span", failures);

        if (statusSpan is not null)
        {
            // The BCL's whole HTTP client convention, plus the one tag the qyl processor owns. The
            // key set is asserted exactly: a key the BCL stops writing, or one qyl starts writing,
            // is a contract change and must fail here.
            RequireExactTagKeys(
                statusSpan,
                [
                    HttpAttributes.RequestMethod,
                    HttpAttributes.ResponseStatusCode,
                    ErrorAttributes.Type,
                    NetworkAttributes.ProtocolVersion,
                    ServerAttributes.Address,
                    ServerAttributes.Port,
                    UrlAttributes.Full,
                    QylAttributes.InstrumentationDomain,
                ],
                failures);
            RequireTag(statusSpan, HttpAttributes.RequestMethod, HttpAttributes.RequestMethodValues.Get, failures);
            RequireTag(statusSpan, HttpAttributes.ResponseStatusCode, "503", failures);
            // error.type on an HTTP error is the status code as a string, not an exception name.
            RequireTag(statusSpan, ErrorAttributes.Type, "503", failures);
            RequireTag(statusSpan, NetworkAttributes.ProtocolVersion, "1.1", failures);
            RequireTag(statusSpan, ServerAttributes.Address, "127.0.0.1", failures);
            RequireTag(statusSpan, ServerAttributes.Port, serverPort.ToString(CultureInfo.InvariantCulture), failures);
            // The query redaction is the BCL's own: the whole query becomes a single asterisk.
            RequireTag(statusSpan, UrlAttributes.Full, $"http://127.0.0.1:{serverPort.ToString(CultureInfo.InvariantCulture)}/probe?*", failures);
            RequireStatus(statusSpan, "Error", failures);
        }

        if (failureSpan is not null)
        {
            // A connection failure negotiates no protocol and returns no status, so the BCL writes
            // neither network.protocol.version nor http.response.status_code.
            RequireExactTagKeys(
                failureSpan,
                [
                    HttpAttributes.RequestMethod,
                    ErrorAttributes.Type,
                    ServerAttributes.Address,
                    ServerAttributes.Port,
                    UrlAttributes.Full,
                    QylAttributes.InstrumentationDomain,
                ],
                failures);
            RequireTag(failureSpan, HttpAttributes.RequestMethod, HttpAttributes.RequestMethodValues.Get, failures);
            RequireTag(failureSpan, ErrorAttributes.Type, "connection_error", failures);
            RequireTag(failureSpan, ServerAttributes.Address, "127.0.0.1", failures);
            RequireTag(failureSpan, ServerAttributes.Port, "1", failures);
            RequireTag(failureSpan, UrlAttributes.Full, "http://127.0.0.1:1/fail?*", failures);
            RequireStatus(failureSpan, "Error", failures);
        }

        foreach (var span in httpSpans)
        {
            // The one tag the qyl processor adds: without it the collector cannot classify the span,
            // because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.HttpClient,
                failures);

            var qylTags = span.Tags.Keys.Count(static key => key.StartsWith("qyl.", StringComparison.Ordinal));
            if (qylTags != 1)
                failures.Add($"expected exactly 1 qyl.* tag on {span.Name}, got {qylTags.ToString(CultureInfo.InvariantCulture)}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");

            // The BCL names the span for the method and keeps the URL out of it.
            if (!StringComparer.Ordinal.Equals(span.Name, HttpAttributes.RequestMethodValues.Get))
                failures.Add($"unexpected high-cardinality span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.OperationName, "System.Net.Http.HttpRequestOut"))
                failures.Add($"unexpected operation name: {span.OperationName}");
        }

        var httpMetrics = metrics
            .Where(static metric =>
                StringComparer.Ordinal.Equals(metric.MeterName, HttpClientTelemetry.MeterName) &&
                StringComparer.Ordinal.Equals(metric.Name, HttpClientTelemetry.RequestDurationInstrument))
            .ToArray();

        if (httpMetrics.Length != 2)
            failures.Add($"expected 2 real HttpClient duration metrics, got {httpMetrics.Length.ToString(CultureInfo.InvariantCulture)}");

        var statusMetric = httpMetrics.FirstOrDefault(static metric =>
            metric.Tags.TryGetValue(HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "503"));
        var failureMetric = httpMetrics.FirstOrDefault(static metric =>
            metric.Tags.TryGetValue(ErrorAttributes.Type, out var errorType) &&
            StringComparer.Ordinal.Equals(errorType, "connection_error"));

        Require(statusMetric, "503 status metric", failures);
        Require(failureMetric, "connection failure metric", failures);
        RequireMetricTag(statusMetric, HttpAttributes.RequestMethod, HttpAttributes.RequestMethodValues.Get, failures);

        foreach (var metric in httpMetrics)
        {
            if (metric.Value < 0)
                failures.Add($"expected non-negative HttpClient metric value, got {metric.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return new HttpClientReport(
            runtimeMode,
            failures.Count is 0,
            failures.ToArray(),
            spanSets,
            excludedScopes,
            httpSpans,
            httpMetrics);
    }

    private static OperationSpanSet SpanSet(
        CapturedSpan[] capturedSpans,
        string operation,
        string[] expected,
        ICollection<string> failures)
    {
        var actual = capturedSpans
            .Where(span => StringComparer.Ordinal.Equals(span.Operation, operation))
            .Where(static span => !span.Scope.StartsWith(ExcludedScopePrefix, StringComparison.Ordinal))
            .Select(static span => span.Tuple)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(sortedExpected, StringComparer.Ordinal))
        {
            failures.Add(
                $"operation {operation} span set mismatch: expected [{string.Join(", ", sortedExpected)}], " +
                $"got [{string.Join(", ", actual)}]");
        }

        return new OperationSpanSet(operation, sortedExpected, actual);
    }

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void Require(CapturedMetric? metric, string label, ICollection<string> failures)
    {
        if (metric is null)
            failures.Add($"missing {label}");
    }

    private static void RequireExactTagKeys(CapturedActivity activity, string[] expected, ICollection<string> failures)
    {
        var actual = activity.Tags.Keys.Order(StringComparer.Ordinal).ToArray();
        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(sortedExpected, StringComparer.Ordinal))
        {
            failures.Add(
                $"span {activity.Name} tag keys mismatch: expected [{string.Join(", ", sortedExpected)}], " +
                $"got [{string.Join(", ", actual)}]");
        }
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

    private static void RequireMetricTag(CapturedMetric? metric, string key, string expected, ICollection<string> failures)
    {
        if (metric is null)
            return;

        if (!metric.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing metric {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected metric {key}={expected}, got {actual}");
    }

    private static void RequireStatus(CapturedActivity activity, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(activity.Status, expected))
            failures.Add($"expected span status {expected}, got {activity.Status}");
    }
}

[JsonSerializable(typeof(HttpClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealHttpClientJsonContext : JsonSerializerContext;
