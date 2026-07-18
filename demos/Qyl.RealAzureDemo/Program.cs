using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure;
using Azure.Storage.Blobs;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

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

var report = AzureReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

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

        if (azureSpans.Length != 2)
            failures.Add($"expected 2 Azure spans, got {azureSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in azureSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "Azure SDK"))
                failures.Add($"unexpected Azure span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Azure span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Azure span status Error, got {span.Status}");

            RequireTag(span, "qyl.instrumentation.domain", "azure.sdk", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, nameof(RequestFailedException), failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes.Full, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes.Path, failures);
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
