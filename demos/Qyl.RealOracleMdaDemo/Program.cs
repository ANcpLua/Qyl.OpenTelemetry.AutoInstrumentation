using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Oracle.ManagedDataAccess.Client;
using Qyl.OpenTelemetry.AutoInstrumentation;

var capturedActivities = new List<CapturedActivity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => capturedActivities.Add(CapturedActivity.From(activity)),
};
ActivitySource.AddActivityListener(activityListener);

try
{
    await using var nonQueryCommand = new OracleCommand("SELECT 1 FROM DUAL");
    _ = nonQueryCommand.ExecuteNonQuery();
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("expected-oraclemda-error=" + exception.GetType().Name);
}

try
{
    await using var scalarCommand = new OracleCommand("SELECT 1 FROM DUAL");
    _ = scalarCommand.ExecuteScalar();
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("expected-oraclemda-error=" + exception.GetType().Name);
}

var report = OracleMdaReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealOracleMdaJsonContext.Default.OracleMdaReport);
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

internal sealed record OracleMdaReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static OracleMdaReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var oracleSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var dbSystem) &&
                StringComparer.Ordinal.Equals(dbSystem, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.OracleDb))
            .ToArray();

        if (oracleSpans.Length != 2)
            failures.Add($"expected 2 Oracle MDA spans, got {oracleSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in oracleSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "DB SELECT"))
                failures.Add($"unexpected Oracle MDA span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected Oracle MDA span kind Client, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Status, "Error"))
                failures.Add($"expected Oracle MDA span status Error, got {span.Status}");

            RequireTag(span, "qyl.instrumentation.domain", "db.client", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.OracleDb, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, "SELECT", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QuerySummary, "SELECT", failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, nameof(InvalidOperationException), failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QueryText, failures);
        }

        return new OracleMdaReport(runtimeMode, failures.Count is 0, failures.ToArray(), oracleSpans);
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

[JsonSerializable(typeof(OracleMdaReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealOracleMdaJsonContext : JsonSerializerContext;
