using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;

var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealAzureDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-azure-demo";
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

var endpoint = new Uri($"http://{IPAddress.Loopback}:9/devstoreaccount1");
var options = new BlobClientOptions
{
    Retry =
    {
        MaxRetries = 0,
        NetworkTimeout = TimeSpan.FromMilliseconds(200),
    },
};
var client = new BlobServiceClient(endpoint, options);

try
{
    _ = client.GetProperties();
}
catch (RequestFailedException exception)
{
    Console.WriteLine("expected-azure-error=" + exception.GetType().Name);
}

try
{
    _ = await client.GetPropertiesAsync();
}
catch (RequestFailedException exception)
{
    Console.WriteLine("expected-azure-error=" + exception.GetType().Name);
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = AzureReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealAzureJsonContext.Default.AzureReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

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

internal sealed record AzureReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static AzureReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var azureSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "azure.sdk"))
            .ToArray();

        if (azureSpans.Length != 4)
            failures.Add($"expected 4 Azure spans, got {azureSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        var transportSpans = azureSpans
            .Where(static span => StringComparer.Ordinal.Equals(span.Kind, "Client"))
            .ToArray();
        var operationSpans = azureSpans
            .Where(static span => StringComparer.Ordinal.Equals(span.Kind, "Internal"))
            .ToArray();
        if (transportSpans.Length != 2)
            failures.Add($"expected 2 Azure transport spans, got {transportSpans.Length.ToString(CultureInfo.InvariantCulture)}");
        if (operationSpans.Length != 2)
            failures.Add($"expected 2 Azure operation spans, got {operationSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in azureSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Azure span status Error, got {span.Status}");

            RequireTag(span, "qyl.instrumentation.domain", "azure.sdk", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, nameof(RequestFailedException), failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes.Full, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes.Path, failures);
        }

        foreach (var span in transportSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "GET"))
                failures.Add($"unexpected Azure transport span name: {span.Name}");
            RequireTag(span, "http.request.method", "GET", failures);
            RequireTag(span, "server.address", "127.0.0.1", failures);
        }

        foreach (var span in operationSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "BlobServiceClient.GetProperties"))
                failures.Add($"unexpected Azure operation span name: {span.Name}");
        }

        return new AzureReport(runtimeMode, failures.Count is 0, failures.ToArray(), azureSpans);
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

    private static void RequireMissingTag(CapturedActivity activity, string key, ICollection<string> failures)
    {
        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(AzureReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealAzureJsonContext : JsonSerializerContext;
