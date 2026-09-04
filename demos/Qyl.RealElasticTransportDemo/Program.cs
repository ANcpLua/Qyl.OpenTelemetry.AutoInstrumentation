using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;
using ElasticAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Elastic.ElasticAttributes;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

// The real registration path: AddQyl subscribes to Elastic.Transport's own ActivitySource and
// installs the one native-span processor, so what the exporter receives is what a consumer receives.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealElasticTransportDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-elastictransport-demo";
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

// A real DistributedTransport against a closed port: the transport starts its own activity before
// the request pipeline, so the span exists even though no node answers.
var configuration = new TransportConfiguration(new Uri($"http://{IPAddress.Loopback}:9"));
var transport = new DistributedTransport(configuration);
var path = new EndpointPath(Elastic.Transport.HttpMethod.GET, "/_search");

var sync = transport.Request<StringResponse>(path, PostData.Empty);
Console.WriteLine("elastictransport-sync=" + sync.ApiCallDetails.HasSuccessfulStatusCode.ToString(CultureInfo.InvariantCulture));

var async = await transport.RequestAsync<StringResponse>(path, PostData.Empty);
Console.WriteLine("elastictransport-async=" + async.ApiCallDetails.HasSuccessfulStatusCode.ToString(CultureInfo.InvariantCulture));

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = ElasticTransportReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealElasticTransportJsonContext.Default.ElasticTransportReport);
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

internal sealed record ElasticTransportReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    // Elastic.Transport's own product registration name. A bare transport reports this; the
    // Elasticsearch client reports its own, which is what moves the span to the db.elasticsearch
    // domain.
    private const string TransportProductName = "elastic-transport-net";

    public static ElasticTransportReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var elasticSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.ElasticTransport))
            .ToArray();

        // One native span per request, not per run.
        if (elasticSpans.Length != 2)
            failures.Add($"expected 2 Elastic.Transport spans, got {elasticSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in elasticSpans)
        {
            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.ElasticTransport,
                failures);

            // Elastic.Transport's own attributes. It emits no database semantic conventions of its
            // own: db.system.name and db.operation.name came from the deleted interceptor's call
            // site and have no equivalent on the native span.
            RequireTag(span, ElasticAttributes.TransportProductName, TransportProductName, failures);
            RequireTag(span, HttpAttributes.RequestMethod, HttpAttributes.RequestMethodValues.Get, failures);
            RequireTag(span, ServerAttributes.Address, IPAddress.Loopback.ToString(), failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Elastic.Transport span kind Client, got {span.Kind}");
        }

        return new ElasticTransportReport(runtimeMode, failures.Count is 0, failures.ToArray(), elasticSpans);
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

[JsonSerializable(typeof(ElasticTransportReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealElasticTransportJsonContext : JsonSerializerContext;
