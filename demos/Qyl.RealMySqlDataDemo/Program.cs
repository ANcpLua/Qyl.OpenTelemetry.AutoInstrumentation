using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using MySql.Data.MySqlClient;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

for (var index = 0; index < 2; index++)
{
    try
    {
        using var command = new MySqlCommand(index is 0 ? "SELECT 1" : "SELECT missing_column");
        _ = command.ExecuteScalar();
    }
    catch (InvalidOperationException exception)
    {
        Console.WriteLine("expected-mysqldata-error=" + exception.GetType().Name);
    }
}

var report = MySqlDataReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealMySqlDataJsonContext.Default.MySqlDataReport);
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

internal sealed record MySqlDataReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static MySqlDataReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var mysqlSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.client") &&
                activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.Mysql))
            .ToArray();

        if (mysqlSpans.Length != 2)
            failures.Add($"expected 2 MySql.Data command spans, got {mysqlSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in mysqlSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "DB SELECT"))
                failures.Add($"unexpected MySql.Data span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected MySql.Data span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected MySql.Data span status Error, got {span.Status}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.Mysql, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, "SELECT", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, nameof(InvalidOperationException), failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QueryText, failures);
        }

        return new MySqlDataReport(runtimeMode, failures.Count is 0, failures.ToArray(), mysqlSpans);
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

[JsonSerializable(typeof(MySqlDataReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealMySqlDataJsonContext : JsonSerializerContext;
