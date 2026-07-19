using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Npgsql;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

var capturedMetrics = new List<CapturedMetric>();
using var meterListener = new MeterListener
{
    InstrumentPublished = static (instrument, listener) =>
    {
        if (instrument.Meter.Name == DemoMetricNames.Database)
            listener.EnableMeasurementEvents(instrument);
    },
};
meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
{
    if (instrument.Name == "db.client.operation.duration")
        capturedMetrics.Add(CapturedMetric.From(instrument, measurement, tags));
});
meterListener.Start();

for (var index = 0; index < 2; index++)
{
    try
    {
        using var command = new NpgsqlCommand(index is 0 ? "SELECT 1" : "SELECT missing_column");
        _ = command.ExecuteScalar();
    }
    catch (InvalidOperationException exception)
    {
        Console.WriteLine("expected-npgsql-error=" + exception.GetType().Name);
    }
}

var report = NpgsqlReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray(),
    capturedMetrics.ToArray());

var json = JsonSerializer.Serialize(report, RealNpgsqlJsonContext.Default.NpgsqlReport);
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

internal sealed record CapturedMetric(
    string Name,
    double Value,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedMetric From(Instrument instrument, double value, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var capturedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
            capturedTags[tag.Key] = Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty;

        return new CapturedMetric(instrument.Name, value, capturedTags);
    }
}

internal sealed record NpgsqlReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities,
    CapturedMetric[] Metrics)
{
    public static NpgsqlReport Create(string runtimeMode, CapturedActivity[] activities, CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        var npgsqlSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.client") &&
                activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.Postgresql))
            .ToArray();
        var npgsqlMetrics = metrics
            .Where(static metric =>
                StringComparer.Ordinal.Equals(metric.Name, "db.client.operation.duration") &&
                metric.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.Postgresql))
            .ToArray();

        if (npgsqlSpans.Length != 2)
            failures.Add($"expected 2 Npgsql command spans, got {npgsqlSpans.Length.ToString(CultureInfo.InvariantCulture)}");
        if (npgsqlMetrics.Length != 2)
            failures.Add($"expected 2 Npgsql duration metric points, got {npgsqlMetrics.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in npgsqlSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "DB SELECT"))
                failures.Add($"unexpected Npgsql span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Npgsql span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Npgsql span status Error, got {span.Status}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.Postgresql, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, "SELECT", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(InvalidOperationException).FullName!, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QueryText, failures);
        }

        foreach (var metric in npgsqlMetrics)
        {
            if (metric.Value < 0)
                failures.Add($"expected non-negative Npgsql duration, got {metric.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return new NpgsqlReport(runtimeMode, failures.Count is 0, failures.ToArray(), npgsqlSpans, npgsqlMetrics);
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

internal static class DemoMetricNames
{
    internal const string Database = "Qyl.OpenTelemetry.AutoInstrumentation.Database";
}

[JsonSerializable(typeof(NpgsqlReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealNpgsqlJsonContext : JsonSerializerContext;
