using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Net;
using System.Net.Sockets;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
var capturedLock = new Lock();
var capturedMetrics = new List<CapturedMetric>();

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

using var meterListener = new MeterListener
{
    InstrumentPublished = static (instrument, listener) =>
    {
        if (StringComparer.Ordinal.Equals(instrument.Meter.Name, DemoMetricNames.HttpClient) &&
            StringComparer.Ordinal.Equals(instrument.Name, "http.client.request.duration"))
        {
            listener.EnableMeasurementEvents(instrument);
        }
    },
};

meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
    capturedMetrics.Add(CapturedMetric.From(instrument, measurement, tags)));
meterListener.Start();

using var httpClient = new HttpClient();
var server = StartOneShotHttpServer(503);
using (await httpClient.GetAsync($"http://127.0.0.1:{server.Port}/probe?token=secret"))
{
}

await server.Completion;

try
{
    await httpClient.GetAsync("http://127.0.0.1:1/fail?token=secret");
}
catch (HttpRequestException exception)
{
    Console.WriteLine($"expected-failure={exception.GetType().Name}");
}

var report = HttpClientReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray(),
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
            await stream.WriteAsync(System.Text.Encoding.ASCII.GetBytes(response));
        }
        finally
        {
            server.Stop();
        }
    });

    return new OneShotHttpServer(port, completion);
}

internal sealed record OneShotHttpServer(int Port, Task Completion);

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
    CapturedActivity[] Activities,
    CapturedMetric[] Metrics)
{
    public static HttpClientReport Create(string runtimeMode, CapturedActivity[] activities, CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        var httpClientSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "http.client"))
            .ToArray();

        if (httpClientSpans.Length != 2)
            failures.Add($"expected 2 real HttpClient spans, got {httpClientSpans.Length}");

        var statusSpan = httpClientSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue("http.response.status_code", out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "503"));
        var failureSpan = httpClientSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue("error.type", out var errorType) &&
            StringComparer.Ordinal.Equals(errorType, "connection_error"));

        Require(statusSpan, "503 status span", failures);
        Require(failureSpan, "connection failure span", failures);
        RequireTag(statusSpan, "http.request.method", "GET", failures);
        RequireTag(statusSpan, "server.address", "127.0.0.1", failures);
        RequireTag(statusSpan, "error.type", "503", failures);
        RequireStatus(statusSpan, "Error", failures);
        RequireStatus(failureSpan, "Error", failures);

        foreach (var span in httpClientSpans)
        {
            if (!span.Tags.ContainsKey("url.full"))
                failures.Add("url.full missing on client span");
            else if (span.Tags["url.full"].Contains('?') && !span.Tags["url.full"].Contains("=Redacted", StringComparison.Ordinal))
                failures.Add("url.full query not redacted by default");

            if (!StringComparer.Ordinal.Equals(span.Name, "GET"))
                failures.Add($"unexpected high-cardinality span name: {span.Name}");
        }

        var httpClientMetrics = metrics
            .Where(static metric =>
                StringComparer.Ordinal.Equals(metric.MeterName, DemoMetricNames.HttpClient) &&
                StringComparer.Ordinal.Equals(metric.Name, "http.client.request.duration"))
            .ToArray();

        if (httpClientMetrics.Length != 2)
            failures.Add($"expected 2 real HttpClient duration metrics, got {httpClientMetrics.Length}");

        var statusMetric = httpClientMetrics.FirstOrDefault(static metric =>
            metric.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Http.HttpAttributes.ResponseStatusCode, out var statusCode) &&
            StringComparer.Ordinal.Equals(statusCode, "503"));
        var failureMetric = httpClientMetrics.FirstOrDefault(static metric =>
            metric.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, out var errorType) &&
            errorType.Length > 0);

        Require(statusMetric, "503 status metric", failures);
        Require(failureMetric, "connection failure metric", failures);
        RequireMetricTag(statusMetric, Qyl.OpenTelemetry.SemanticConventions.Attributes.Http.HttpAttributes.RequestMethod, Qyl.OpenTelemetry.SemanticConventions.Attributes.Http.HttpAttributes.RequestMethodValues.Get, failures);

        foreach (var metric in httpClientMetrics)
        {
            if (metric.Value < 0)
                failures.Add($"expected non-negative HttpClient metric value, got {metric.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return new HttpClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities, httpClientMetrics);
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

    private static void RequireStatus(CapturedActivity? activity, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!StringComparer.Ordinal.Equals(activity.Status, expected))
            failures.Add($"expected span status {expected}, got {activity.Status}");
    }
}

internal static class DemoMetricNames
{
    internal const string HttpClient = "System.Net.Http";
}

[JsonSerializable(typeof(HttpClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealHttpClientJsonContext : JsonSerializerContext;
