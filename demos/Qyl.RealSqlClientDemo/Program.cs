using System.Diagnostics;
using System.Diagnostics.Metrics;
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
var capturedLock = new Lock();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            captured.Add(CapturedActivity.From(activity));
        }
    },
};

ActivitySource.AddActivityListener(listener);

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

await WaitForSqlServerAsync(connectionString);

await using (var connection = new SqlConnection(connectionString))
{
    await connection.OpenAsync();

    ExecuteNonQuery(connection, "CREATE TABLE #QylProbe (Id int NOT NULL, Name nvarchar(32) NOT NULL)");
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
    Environment.GetEnvironmentVariable("QYL_SQLCLIENT_EXPECTED_TRACE_OWNER") ?? "source_interceptor",
    Environment.GetEnvironmentVariable("QYL_SQLCLIENT_EXPECTED_PORT") ?? "11433",
    captured.ToArray(),
    capturedMetrics.ToArray());

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

static void ExecuteNonQuery(SqlConnection connection, string sql)
{
    using var command = connection.CreateCommand();
    command.CommandText = sql;
    command.ExecuteNonQuery();
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
    double DurationSeconds,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.Duration.TotalSeconds,
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

internal sealed record SqlClientReport(
    string RuntimeMode,
    string TraceOwner,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities,
    CapturedMetric[] Metrics)
{
    public static SqlClientReport Create(
        string runtimeMode,
        string traceOwner,
        string expectedServerPort,
        CapturedActivity[] activities,
        CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        var sourceInterceptorExpected = StringComparer.Ordinal.Equals(traceOwner, "source_interceptor");
        if (!sourceInterceptorExpected && !StringComparer.Ordinal.Equals(traceOwner, "specialist_listener"))
            failures.Add($"unknown trace owner: {traceOwner}");
        var expectedDomain = sourceInterceptorExpected ? "db.client" : "db.sqlclient";
        var sqlSpans = activities
            .Where(static activity =>
                activity.Tags.ContainsKey("qyl.instrumentation.domain"))
            .Where(activity => StringComparer.Ordinal.Equals(activity.Tags["qyl.instrumentation.domain"], expectedDomain))
            .ToArray();
        var sqlMetrics = metrics
            .Where(static metric =>
                StringComparer.Ordinal.Equals(metric.Name, "db.client.operation.duration") &&
                metric.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemNameValues.MicrosoftSqlServer))
            .ToArray();

        var expectedMetricCount = sourceInterceptorExpected ? 4 : 0;
        if (activities.Length != 4)
            failures.Add($"expected exactly 4 captured qyl activities, got {activities.Length}");
        if (sqlSpans.Length != 4)
            failures.Add($"expected 4 {traceOwner} SqlClient command spans, got {sqlSpans.Length}");
        if (metrics.Length != expectedMetricCount)
            failures.Add($"expected exactly {expectedMetricCount} captured qyl metrics, got {metrics.Length}");
        if (sqlMetrics.Length != expectedMetricCount)
            failures.Add($"expected {expectedMetricCount} SqlClient duration metric points, got {sqlMetrics.Length}");

        var successSelect = FindByOperationAndStatus(sqlSpans, "SELECT", "Unset");
        var errorSelect = FindByOperationAndStatus(sqlSpans, "SELECT", "Error");

        Require(successSelect, "successful SELECT span", failures);
        Require(errorSelect, "error SELECT span", failures);
        RequireTag(successSelect, "db.system.name", "microsoft.sql_server", failures);
        RequireTag(successSelect, "db.namespace", "tempdb", failures);
        RequireTag(successSelect, "db.operation.name", "SELECT", failures);
        RequireTag(successSelect, "db.query.summary", sourceInterceptorExpected ? "SELECT" : "Text SELECT", failures);
        if (!sourceInterceptorExpected)
        {
            RequireTag(successSelect, "server.address", "127.0.0.1", failures);
            RequireTag(successSelect, "server.port", expectedServerPort, failures);
        }
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
        RequireTag(errorSelect, "error.type", sourceInterceptorExpected ? typeof(SqlException).FullName! : "208", failures);
        RequireTag(errorSelect, "db.operation.name", "SELECT", failures);

        foreach (var span in sqlSpans)
        {
            var expectedPrefix = sourceInterceptorExpected ? "DB " : "SQL ";
            if (!span.Name.StartsWith(expectedPrefix, StringComparison.Ordinal))
                failures.Add($"unexpected SqlClient span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, ActivityKind.Client.ToString()))
                failures.Add($"expected SqlClient span kind Client, got {span.Kind}");
            if (span.DurationSeconds <= 0)
                failures.Add($"expected positive SqlClient span duration, got {span.DurationSeconds.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var metric in sqlMetrics)
        {
            if (metric.Value <= 0)
                failures.Add($"expected positive SqlClient duration, got {metric.Value.ToString(CultureInfo.InvariantCulture)}");
        }

        return new SqlClientReport(runtimeMode, traceOwner, failures.Count is 0, failures.ToArray(), activities, metrics);
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

internal static class DemoMetricNames
{
    internal const string Database = "Qyl.OpenTelemetry.AutoInstrumentation.Database";
}

[JsonSerializable(typeof(SqlClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealSqlClientJsonContext : JsonSerializerContext;
