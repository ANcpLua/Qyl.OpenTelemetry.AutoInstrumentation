using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.RealEfCoreDemo;

var captured = new List<CapturedActivity>();

using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

await using var connection = new SqliteConnection("Data Source=:memory:");
await connection.OpenAsync();
await CreateSchemaAsync(connection);

await using (var db = new ProbeContext(connection))
{
    await db.Database.ExecuteSqlRawAsync("INSERT INTO Items (Name) VALUES ('alpha')");
    await db.Database.ExecuteSqlRawAsync("UPDATE Items SET Name = 'beta' WHERE Name = 'alpha'");

    try
    {
        await db.Database.ExecuteSqlRawAsync("SELECT * FROM missing_table");
    }
    catch (SqliteException exception)
    {
        Console.WriteLine($"expected-failure={exception.GetType().Name}");
    }
}

var report = EfCoreReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealEfCoreJsonContext.Default.EfCoreReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task CreateSchemaAsync(SqliteConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE Items (
            Id INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL
        );
        """;
    await command.ExecuteNonQueryAsync();
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
                static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record EfCoreReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static EfCoreReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var efCoreSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.efcore"))
            .ToArray();

        if (efCoreSpans.Length != 3)
            failures.Add($"expected 3 real EFCore spans, got {efCoreSpans.Length}");

        var insertSpan = FindByOperation(efCoreSpans, "INSERT");
        var updateSpan = FindByOperation(efCoreSpans, "UPDATE");
        var errorSpan = efCoreSpans.FirstOrDefault(static activity =>
            activity.Tags.TryGetValue("error.type", out var errorType) &&
            StringComparer.Ordinal.Equals(errorType, "Microsoft.Data.Sqlite.SqliteException"));

        Require(insertSpan, "INSERT span", failures);
        Require(updateSpan, "UPDATE span", failures);
        Require(errorSpan, "SqliteException span", failures);
        RequireTag(insertSpan, "db.system.name", "sqlite", failures);
        RequireTag(updateSpan, "db.system.name", "sqlite", failures);
        RequireTag(insertSpan, "db.operation.name", "INSERT", failures);
        RequireTag(updateSpan, "db.operation.name", "UPDATE", failures);
        RequireTag(insertSpan, "db.query.summary", "ExecuteSqlRaw INSERT", failures);
        RequireTag(updateSpan, "db.query.summary", "ExecuteSqlRaw UPDATE", failures);
        RequireTag(errorSpan, "db.operation.name", "SELECT", failures);
        RequireStatus(insertSpan, "Unset", failures);
        RequireStatus(updateSpan, "Unset", failures);
        RequireStatus(errorSpan, "Error", failures);

        foreach (var span in efCoreSpans)
        {
            if (span.Tags.ContainsKey("db.query.text"))
                failures.Add("db.query.text leaked with default privacy policy");

            if (!span.Name.StartsWith("DB ", StringComparison.Ordinal))
                failures.Add($"unexpected EFCore span name: {span.Name}");
        }

        return new EfCoreReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

    private static CapturedActivity? FindByOperation(
        IEnumerable<CapturedActivity> activities,
        string operation)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue("db.operation.name", out var actual) &&
            StringComparer.Ordinal.Equals(actual, operation));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequireStatus(CapturedActivity? activity, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!StringComparer.Ordinal.Equals(activity.Status, expected))
            failures.Add($"expected span status {expected}, got {activity.Status}");
    }
}

[JsonSerializable(typeof(EfCoreReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealEfCoreJsonContext : JsonSerializerContext;
