using System.Data.Common;
using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

using DbConnection connection = new SqliteConnection("Data Source=:memory:");
connection.Open();
var databaseNamespace = connection.Database;

ExecuteNonQuery(connection, "CREATE TABLE Probe (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL)");
ExecuteNonQuery(connection, "INSERT INTO Probe (Id, Name) VALUES (1, 'alpha')");
var value = ExecuteScalar(connection, "SELECT Name FROM Probe WHERE Id = 1");
Console.WriteLine("adonet-value=" + Convert.ToString(value, CultureInfo.InvariantCulture));

try
{
    _ = ExecuteScalar(connection, "SELECT Name FROM MissingProbe WHERE Id = 1");
}
catch (SqliteException exception)
{
    Console.WriteLine("expected-adonet-error=" + exception.GetType().Name);
}

var report = AdoNetReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    databaseNamespace,
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealAdoNetJsonContext.Default.AdoNetReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static int ExecuteNonQuery(DbConnection connection, string sql)
{
    using DbCommand command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteNonQuery();
}

static object? ExecuteScalar(DbConnection connection, string sql)
{
    using DbCommand command = connection.CreateCommand();
    command.CommandText = sql;
    return command.ExecuteScalar();
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

internal sealed record AdoNetReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static AdoNetReport Create(
        string runtimeMode,
        string databaseNamespace,
        CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var adoNetSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "db.client") &&
                activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.OtherSql))
            .ToArray();

        if (adoNetSpans.Length != 4)
            failures.Add($"expected 4 generic ADO.NET command spans, got {adoNetSpans.Length}");

        var create = FindByOperationAndStatus(adoNetSpans, "CREATE", "Unset");
        var insert = FindByOperationAndStatus(adoNetSpans, "INSERT", "Unset");
        var selectSuccess = FindByOperationAndStatus(adoNetSpans, "SELECT", "Unset");
        var selectError = FindByOperationAndStatus(adoNetSpans, "SELECT", "Error");

        Require(create, "successful CREATE span", failures);
        Require(insert, "successful INSERT span", failures);
        Require(selectSuccess, "successful SELECT span", failures);
        Require(selectError, "error SELECT span", failures);
        RequireTag(selectError, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, nameof(SqliteException), failures);

        foreach (var span in adoNetSpans)
        {
            if (span.Name is not "DB CREATE" and not "DB INSERT" and not "DB SELECT")
                failures.Add($"unexpected ADO.NET span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.SystemName, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes.SystemNameValues.OtherSql, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.Namespace, databaseNamespace, failures);
            RequireMissingTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.QueryText, failures);
        }

        return new AdoNetReport(runtimeMode, failures.Count is 0, failures.ToArray(), adoNetSpans);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes.OperationName, out var actual) &&
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

    private static void RequireMissingTag(CapturedActivity activity, string key, ICollection<string> failures)
    {
        if (activity.Tags.ContainsKey(key))
            failures.Add($"unexpected {key}");
    }
}

[JsonSerializable(typeof(AdoNetReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealAdoNetJsonContext : JsonSerializerContext;
