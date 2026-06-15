using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

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

var report = ElasticsearchReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealElasticsearchJsonContext.Default.ElasticsearchReport);
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

internal sealed record ElasticsearchReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static ElasticsearchReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var elasticsearchSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.DbElasticsearch))
            .ToArray();

        if (elasticsearchSpans.Length != 2)
            failures.Add($"expected 2 Elasticsearch spans, got {elasticsearchSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in elasticsearchSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "Elasticsearch request"))
                failures.Add($"unexpected Elasticsearch span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Elasticsearch span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Elasticsearch span status Error, got {span.Status}");

            RequireTag(span, QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemElasticsearch, failures);
            RequireTag(span, QylSemanticAttributes.DbOperationName, "request", failures);
            RequireTag(span, QylSemanticAttributes.ErrorType, nameof(TransportException), failures);
            RequireMissingTag(span, QylSemanticAttributes.DbQueryText, failures);
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

    private static void RequireMissingTag(CapturedActivity activity, string key, ICollection<string> failures)
    {
        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(ElasticsearchReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealElasticsearchJsonContext : JsonSerializerContext;
