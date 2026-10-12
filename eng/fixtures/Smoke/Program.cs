using System.Diagnostics;
#if QYL_SDK_PACKAGE_SMOKE
using System.Diagnostics.Metrics;
#endif
using System.Net;
using System.Net.Sockets;
using System.Text;
#if QYL_SDK_PACKAGE_SMOKE
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
#endif
#if QYL_SDK_PACKAGE_SMOKE
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using Qyl;
#endif
using Qyl.Telemetry.AutoInstrumentation;
#if QYL_PROJECTREFERENCE_SMOKE
using Qyl.Telemetry.AutoInstrumentation.Hosting;
#endif

#if QYL_PROJECTREFERENCE_SMOKE
QylAutoInstrumentationBootstrap.Boot();
#endif

#if QYL_SDK_PACKAGE_SMOKE
const string qylActivitySourceName = "Qyl.Telemetry.AutoInstrumentation";
const string httpClientMeterName = "System.Net.Http";
const string httpClientDurationMetricName = "http.client.request.duration";

var exportedActivities = new List<Activity>();
var exportedMetrics = new List<Metric>();
var inMemoryMetricReader = new BaseExportingMetricReader(new InMemoryExporter<Metric>(exportedMetrics));
var rawHttpClientDurationMeasurementCount = 0;
var applicationServerPort = 0;

var qylBuilder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "qyl-package-smoke",
    DisableDefaults = true,
});
qylBuilder.AddQyl(options =>
{
    options.ServiceName = "qyl-package-smoke";
    options.EnableCollectorDiscovery = false;
    options.EnableMetricsExport = true;
    options.EnableLogExport = false;
    options.EnableSessionTagInheritance = false;
});
if (!qylBuilder.Services.Any(static descriptor => descriptor.ServiceType == typeof(IStartupFilter)))
    throw new InvalidOperationException("Qyl.Telemetry.Hosting AddQyl() did not register the ASP.NET Core startup filter.");
qylBuilder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities))
    .WithMetrics(metrics => metrics.AddReader(inMemoryMetricReader));

using var meterListener = new MeterListener
{
    InstrumentPublished = (instrument, listener) =>
    {
        if (StringComparer.Ordinal.Equals(instrument.Meter.Name, httpClientMeterName) &&
            StringComparer.Ordinal.Equals(instrument.Name, httpClientDurationMetricName))
        {
            listener.EnableMeasurementEvents(instrument);
        }
    },
};
meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, state) =>
{
    _ = instrument;
    _ = measurement;
    _ = state;
    if (IsRawApplicationMeasurement(tags, Volatile.Read(ref applicationServerPort)))
        Interlocked.Increment(ref rawHttpClientDurationMeasurementCount);
});
meterListener.Start();

using var qylHost = qylBuilder.Build();
await qylHost.StartAsync();
#endif

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(listener);

await using var server = LoopbackHttpServer.Start();
#if QYL_SDK_PACKAGE_SMOKE
Volatile.Write(ref applicationServerPort, server.Uri.Port);
#endif
using var http = new HttpClient();
var response = await http.GetAsync(server.Uri + "probe?secret=redacted");
await server.RequestCompleted;
Console.WriteLine("http.status=" + ((int)response.StatusCode).ToString(System.Globalization.CultureInfo.InvariantCulture));

#if QYL_SDK_PACKAGE_SMOKE
if (!inMemoryMetricReader.Collect(5_000))
    throw new InvalidOperationException("The in-memory metric reader did not complete collection.");

var exportedQylHttpClientSpanCount = exportedActivities.Count(activity =>
    StringComparer.Ordinal.Equals(activity.Source.Name, qylActivitySourceName) &&
    activity.GetTagItem("qyl.instrumentation.domain") is string domain &&
    StringComparer.Ordinal.Equals(domain, "http.client") &&
    IsApplicationActivity(activity.TagObjects, applicationServerPort));
var exportedNativeHttpClientSpanCount = exportedActivities.Count(activity =>
    StringComparer.Ordinal.Equals(activity.Source.Name, httpClientMeterName) &&
    IsApplicationActivity(activity.TagObjects, applicationServerPort));
var exportedHttpClientDurationMetricCount = 0;
var exportedHttpClientDurationSeriesCount = 0;
long exportedHttpClientDurationMeasurementCount = 0;
foreach (var metric in exportedMetrics)
{
    if (!StringComparer.Ordinal.Equals(metric.MeterName, httpClientMeterName) ||
        !StringComparer.Ordinal.Equals(metric.Name, httpClientDurationMetricName))
    {
        continue;
    }

    exportedHttpClientDurationMetricCount++;
    foreach (ref readonly var point in metric.GetMetricPoints())
    {
        if (!IsApplicationMetricSeries(point.Tags, applicationServerPort))
            continue;

        exportedHttpClientDurationSeriesCount++;
        exportedHttpClientDurationMeasurementCount += point.GetHistogramCount();
    }
}

// 15.0.0 handed the client lane to System.Net.Http's own source: exactly one span per request,
// the library's own, with the qyl domain stamped onto it by QylNativeSpanProcessor. A qyl-made
// HttpClient span reappearing here is the duplicate this release exists to remove.
if (exportedQylHttpClientSpanCount != 0 ||
    exportedNativeHttpClientSpanCount != 1 ||
    rawHttpClientDurationMeasurementCount != 1 ||
    exportedHttpClientDurationMetricCount != 1 ||
    exportedHttpClientDurationSeriesCount != 1 ||
    exportedHttpClientDurationMeasurementCount != 1)
{
    throw new InvalidOperationException(
        "Unexpected Qyl.Telemetry.Hosting telemetry composition: " +
        $"qylHttpClientSpans={exportedQylHttpClientSpanCount}, " +
        $"nativeHttpClientSpans={exportedNativeHttpClientSpanCount}, " +
        $"rawHttpClientDurationMeasurements={rawHttpClientDurationMeasurementCount}, " +
        $"exportedHttpClientDurationMetrics={exportedHttpClientDurationMetricCount}, " +
        $"exportedHttpClientDurationSeries={exportedHttpClientDurationSeriesCount}, " +
        $"exportedHttpClientDurationMeasurements={exportedHttpClientDurationMeasurementCount}.");
}

await qylHost.StopAsync();
#endif


foreach (var activity in captured.OrderBy(static activity => activity.DisplayName, StringComparer.Ordinal))
{
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);

    tags.TryGetValue("qyl.instrumentation.domain", out var domain);
    tags.TryGetValue("http.request.method", out var method);
    tags.TryGetValue("http.response.status_code", out var statusCode);

    Console.WriteLine("activity=" + activity.DisplayName + "|" + activity.Kind + "|" + domain + "|" + method + "|" + statusCode);
}

Console.WriteLine("activity.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

// The qyl ActivitySource emits nothing for an HttpClient call as of 15.0.0: the client lane is
// System.Net.Http's own source, and a span appearing here again would be the duplicate this
// release removed. The generator still reaching this consumer is proven by the interceptor
// source, not by a qyl-made span.
return captured.Count == 0 ? 0 : 3;


#if QYL_SDK_PACKAGE_SMOKE
static bool IsApplicationActivity(
    IEnumerable<KeyValuePair<string, object?>> tags,
    int expectedPort)
{
    var addressMatches = false;
    var portMatches = false;
    foreach (var tag in tags)
    {
        if (StringComparer.Ordinal.Equals(tag.Key, "server.address"))
            addressMatches = StringComparer.Ordinal.Equals(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture), "127.0.0.1");
        else if (StringComparer.Ordinal.Equals(tag.Key, "server.port"))
            portMatches = Convert.ToInt32(tag.Value, System.Globalization.CultureInfo.InvariantCulture) == expectedPort;
    }

    return addressMatches && portMatches;
}

static bool IsRawApplicationMeasurement(
    ReadOnlySpan<KeyValuePair<string, object?>> tags,
    int expectedPort)
{
    var addressMatches = false;
    var portMatches = false;
    foreach (var tag in tags)
    {
        if (StringComparer.Ordinal.Equals(tag.Key, "server.address"))
            addressMatches = StringComparer.Ordinal.Equals(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture), "127.0.0.1");
        else if (StringComparer.Ordinal.Equals(tag.Key, "server.port"))
            portMatches = Convert.ToInt32(tag.Value, System.Globalization.CultureInfo.InvariantCulture) == expectedPort;
    }

    return addressMatches && portMatches;
}

static bool IsApplicationMetricSeries(
    OpenTelemetry.ReadOnlyTagCollection tags,
    int expectedPort)
{
    var addressMatches = false;
    var portMatches = false;
    foreach (var tag in tags)
    {
        if (StringComparer.Ordinal.Equals(tag.Key, "server.address"))
            addressMatches = StringComparer.Ordinal.Equals(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture), "127.0.0.1");
        else if (StringComparer.Ordinal.Equals(tag.Key, "server.port"))
            portMatches = Convert.ToInt32(tag.Value, System.Globalization.CultureInfo.InvariantCulture) == expectedPort;
    }

    return addressMatches && portMatches;
}
#endif

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private LoopbackHttpServer(TcpListener listener)
    {
        _listener = listener;
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", UriKind.Absolute);
        RequestCompleted = ServeOnceAsync(listener);
    }

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

// A never-executed call site on a surviving interceptor lane. The smoke test asserts that the
// generator reaches a package consumer at all, and 15.0.0 moved the HttpClient lane to
// System.Net.Http's own source — so the HTTP work above no longer produces an interceptor and
// something else has to carry that proof. DbCommand is in the BCL, so this costs no dependency
// and never runs.
internal static class QylInterceptorProbe
{
    internal static void Emit(System.Data.Common.DbCommand command) => _ = command.ExecuteScalar();
}
