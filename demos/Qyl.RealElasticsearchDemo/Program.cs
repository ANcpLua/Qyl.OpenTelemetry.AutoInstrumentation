using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;
using ElasticAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Elastic.ElasticAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

// The real registration path. Elastic.Clients.Elasticsearch owns no ActivitySource: its spans are
// Elastic.Transport's, enriched by the client, which is why the demo subscribes to the transport.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealElasticsearchDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-elasticsearch-demo";
    // The live-check gate points this at its OTLP listener. Unset, the demo exports into a
    // closed port and asserts on its in-memory exporter alone.
    options.CollectorEndpoint =
        new Uri(Environment.GetEnvironmentVariable("QYL_LIVE_CHECK_ENDPOINT") ?? "http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

var endpoint = new Uri($"http://{IPAddress.Loopback}:9");
var settings = new ElasticsearchClientSettings(endpoint)
    .ThrowExceptions()
    .RequestTimeout(TimeSpan.FromMilliseconds(200));
var client = new ElasticsearchClient(settings);

try
{
    _ = client.Ping();
}
catch (TransportException exception)
{
    Console.WriteLine("expected-elasticsearch-error=" + exception.GetType().Name);
}

try
{
    _ = await client.PingAsync();
}
catch (TransportException exception)
{
    Console.WriteLine("expected-elasticsearch-error=" + exception.GetType().Name);
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = ElasticsearchReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealElasticsearchJsonContext.Default.ElasticsearchReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

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

internal sealed record ElasticsearchReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    // The client's own product registration name. It is what tells an Elasticsearch call from a bare
    // transport call on the one source both share; it reaches the collector as a vendor
    // pass-through tag, which is where that distinction is now drawn.
    private const string ElasticsearchProductName = "elasticsearch-net";

    public static ElasticsearchReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var elasticsearchSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.ElasticTransport))
            .ToArray();

        // One native span per request, not per run.
        if (elasticsearchSpans.Length != 2)
            failures.Add($"expected 2 Elasticsearch spans, got {elasticsearchSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in elasticsearchSpans)
        {
            // The one attribute the qyl processor owns. Both integrations share the Elastic.Transport
            // source and therefore one table row, so the domain is the source's, not the client's.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.ElasticTransport,
                failures);
            RequireTag(span, ElasticAttributes.TransportProductName, ElasticsearchProductName, failures);
            RequireTag(span, ServerAttributes.Address, IPAddress.Loopback.ToString(), failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Elasticsearch span kind Client, got {span.Kind}");
        }

        return new ElasticsearchReport(runtimeMode, failures.Count is 0, failures.ToArray(), elasticsearchSpans);
    }

    private static void RequireTag(CapturedActivity activity, string key, string expected, ICollection<string> failures)
    {
        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(ElasticsearchReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealElasticsearchJsonContext : JsonSerializerContext;
