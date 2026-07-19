using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Elastic.Transport;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

ITransport transport = new ThrowingTransport();
var path = new EndpointPath(Elastic.Transport.HttpMethod.GET, "/_search");

try
{
    _ = transport.Request<StringResponse>(path, PostData.Empty);
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("expected-elastictransport-error=" + exception.GetType().Name);
}

try
{
    _ = await transport.RequestAsync<StringResponse>(path, PostData.Empty);
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("expected-elastictransport-error=" + exception.GetType().Name);
}

var report = ElasticTransportReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealElasticTransportJsonContext.Default.ElasticTransportReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

internal sealed class ThrowingTransport : ITransport
{
    public ITransportConfiguration Configuration => null!;

    public TResponse Request<TResponse>(in EndpointPath path, PostData? postData = null, Action<Activity>? configureActivity = null, IRequestConfiguration? localConfiguration = null)
        where TResponse : TransportResponse, new()
        => throw new InvalidOperationException("qyl-elastictransport-error");

    public Task<TResponse> RequestAsync<TResponse>(in EndpointPath path, PostData? postData = null, Action<Activity>? configureActivity = null, IRequestConfiguration? localConfiguration = null, CancellationToken cancellationToken = default)
        where TResponse : TransportResponse, new()
        => throw new InvalidOperationException("qyl-elastictransport-error");
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

internal sealed record ElasticTransportReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static ElasticTransportReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var elasticSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "elastic.transport"))
            .ToArray();

        if (elasticSpans.Length != 2)
            failures.Add($"expected 2 Elastic.Transport spans, got {elasticSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in elasticSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "Elastic transport request"))
                failures.Add($"unexpected Elastic.Transport span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Elastic.Transport span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Elastic.Transport span status Error, got {span.Status}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.Elasticsearch, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, "request", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(InvalidOperationException).FullName!, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QueryText, failures);
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

    private static void RequireMissingTag(CapturedActivity activity, string key, ICollection<string> failures)
    {
        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(ElasticTransportReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealElasticTransportJsonContext : JsonSerializerContext;
