using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.SqlClient;
using Qyl.OpenTelemetry.AutoInstrumentation;

var connectionString = Environment.GetEnvironmentVariable("QYL_SQLCLIENT_CONNECTION_STRING");
if (string.IsNullOrWhiteSpace(connectionString))
{
    Console.Error.WriteLine("QYL_SQLCLIENT_CONNECTION_STRING is required.");
    return 2;
}

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

await WaitForSqlServerAsync(connectionString);

await using (var connection = new SqlConnection(connectionString))
{
    await connection.OpenAsync();

    await ExecuteNonQueryAsync(connection, "CREATE TABLE #QylProbe (Id int NOT NULL, Name nvarchar(32) NOT NULL)");
    await ExecuteNonQueryAsync(connection, "INSERT INTO #QylProbe (Id, Name) VALUES (1, N'alpha')");
    _ = await ExecuteScalarAsync(connection, "SELECT Id FROM #QylProbe WHERE Id = 1");

    try
    {
        _ = await ExecuteScalarAsync(connection, "SELECT * FROM dbo.QylMissingTable");
    }
    catch (SqlException exception) when (exception.Number == 208)
    {
        Console.WriteLine("expected-sql-error=208");
    }
}

var report = SqlClientReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    Environment.GetEnvironmentVariable("QYL_SQLCLIENT_EXPECTED_PORT") ?? "11433",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealSqlClientJsonContext.Default.SqlClientReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForSqlServerAsync(string connectionString)
{
    Exception? lastException = null;

    for (var attempt = 0; attempt < 30; attempt++)
    {
        try
        {
            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();
            return;
        }
        catch (SqlException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("SQL Server did not become ready.", lastException);
}

static async Task ExecuteNonQueryAsync(SqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    await command.ExecuteNonQueryAsync();
}

static async Task<object?> ExecuteScalarAsync(SqlConnection connection, string sql)
{
    await using var command = connection.CreateCommand();
    command.CommandText = sql;
    return await command.ExecuteScalarAsync();
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

internal sealed record SqlClientReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static SqlClientReport Create(string runtimeMode, string expectedServerPort, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var sqlSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.sqlclient"))
            .ToArray();

        if (sqlSpans.Length != 4)
            failures.Add($"expected 4 real SqlClient command spans, got {sqlSpans.Length}");

        var successSelect = FindByOperationAndStatus(sqlSpans, "SELECT", "Unset");
        var errorSelect = FindByOperationAndStatus(sqlSpans, "SELECT", "Error");

        Require(successSelect, "successful SELECT span", failures);
        Require(errorSelect, "error SELECT span", failures);
        RequireTag(successSelect, "db.system.name", "microsoft.sql_server", failures);
        RequireTag(successSelect, "db.namespace", "tempdb", failures);
        RequireTag(successSelect, "db.operation.name", "SELECT", failures);
        RequireTag(successSelect, "db.query.summary", "Text SELECT", failures);
        RequireTag(successSelect, "server.address", "127.0.0.1", failures);
        RequireTag(successSelect, "server.port", expectedServerPort, failures);
        if (string.Equals(
                Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            RequireTagPrefix(successSelect, "db.query.text", "SELECT", failures);
        }
        else
        {
            RequireMissingTag(successSelect, "db.query.text", failures);
        }
        RequireTag(errorSelect, "error.type", "208", failures);
        RequireTag(errorSelect, "db.operation.name", "SELECT", failures);

        foreach (var span in sqlSpans)
        {
            if (span.Name is not "SQL CREATE" and not "SQL INSERT" and not "SQL SELECT")
                failures.Add($"unexpected SqlClient span name: {span.Name}");
        }

        return new SqlClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
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

    private static void RequireTagPrefix(CapturedActivity? activity, string key, string expectedPrefix, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!actual.StartsWith(expectedPrefix, StringComparison.Ordinal))
            failures.Add($"expected {key} starting with {expectedPrefix}, got {actual}");
    }

    private static void RequireMissingTag(CapturedActivity? activity, string key, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(SqlClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealSqlClientJsonContext : JsonSerializerContext;
