using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qyl.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

using var connection = new ProbeAdoNetConnection();
connection.Open();

ExecuteNonQuery(connection, "CREATE TABLE Probe (Id INTEGER NOT NULL PRIMARY KEY, Name TEXT NOT NULL)");
ExecuteNonQuery(connection, "INSERT INTO Probe (Id, Name) VALUES (1, 'alpha')");
var value = ExecuteScalar(connection, "SELECT Name FROM Probe WHERE Id = 1");
Console.WriteLine("adonet-value=" + Convert.ToString(value, CultureInfo.InvariantCulture));

try
{
    _ = ExecuteScalar(connection, "SELECT Name FROM MissingProbe WHERE Id = 1");
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("expected-adonet-error=" + exception.GetType().Name);
}

var report = AdoNetReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealAdoNetJsonContext.Default.AdoNetReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static int ExecuteNonQuery(ProbeAdoNetConnection connection, string sql)
{
    using var command = connection.CreateProbeCommand(sql);
    return command.ExecuteNonQuery();
}

static object? ExecuteScalar(ProbeAdoNetConnection connection, string sql)
{
    using var command = connection.CreateProbeCommand(sql);
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
    public static AdoNetReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var adoNetSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.DbClient) &&
                activity.Tags.TryGetValue(QylSemanticAttributes.DbSystemName, out var system) &&
                StringComparer.Ordinal.Equals(system, QylSemanticAttributes.DbSystemOtherSql))
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
        RequireTag(selectError, QylSemanticAttributes.ErrorType, nameof(InvalidOperationException), failures);

        foreach (var span in adoNetSpans)
        {
            if (span.Name is not "DB CREATE" and not "DB INSERT" and not "DB SELECT")
                failures.Add($"unexpected ADO.NET span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Client"))
                failures.Add($"expected kind Client, got {span.Kind}");

            RequireTag(span, QylSemanticAttributes.DbSystemName, QylSemanticAttributes.DbSystemOtherSql, failures);
            RequireTag(span, QylSemanticAttributes.DbNamespace, ProbeAdoNetConnection.DatabaseName, failures);
            RequireMissingTag(span, QylSemanticAttributes.DbQueryText, failures);
        }

        return new AdoNetReport(runtimeMode, failures.Count is 0, failures.ToArray(), adoNetSpans);
    }

    private static CapturedActivity? FindByOperationAndStatus(IEnumerable<CapturedActivity> activities, string operation, string status)
        => activities.FirstOrDefault(activity =>
            StringComparer.Ordinal.Equals(activity.Status, status) &&
            activity.Tags.TryGetValue(QylSemanticAttributes.DbOperationName, out var actual) &&
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

internal sealed class ProbeAdoNetConnection : DbConnection
{
    public const string DatabaseName = "qyl-adonet";
    private ConnectionState _state = ConnectionState.Closed;

    [AllowNull]
    public override string ConnectionString { get; set; } = "Data Source=qyl-adonet";

    public override string Database => DatabaseName;

    public override string DataSource => "qyl-adonet-source";

    public override string ServerVersion => "1.0";

    public override ConnectionState State => _state;

    public ProbeAdoNetCommand CreateProbeCommand(string commandText)
        => new(this) { CommandText = commandText };

    public override void ChangeDatabase(string databaseName)
    {
    }

    public override void Close()
        => _state = ConnectionState.Closed;

    public override void Open()
        => _state = ConnectionState.Open;

    protected override DbTransaction BeginDbTransaction(IsolationLevel isolationLevel)
        => throw new NotSupportedException();

    protected override DbCommand CreateDbCommand()
        => new ProbeAdoNetCommand(this);
}

internal sealed class ProbeAdoNetCommand(ProbeAdoNetConnection connection) : DbCommand
{
    private readonly ProbeAdoNetParameterCollection _parameters = new();
    private DbConnection? _connection = connection;

    [AllowNull]
    public override string CommandText { get; set; } = string.Empty;

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection
    {
        get => _connection;
        set => _connection = value;
    }

    protected override DbParameterCollection DbParameterCollection => _parameters;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery()
        => FirstToken(CommandText) switch
        {
            "CREATE" => 0,
            "INSERT" => 1,
            _ => throw new InvalidOperationException("unsupported non-query command"),
        };

    public override object? ExecuteScalar()
    {
        if (CommandText.Contains("MissingProbe", StringComparison.Ordinal))
            throw new InvalidOperationException("qyl-adonet-error");

        return FirstToken(CommandText) == "SELECT"
            ? "alpha"
            : null;
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter()
        => new ProbeAdoNetParameter();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
        => throw new NotSupportedException();

    private static string FirstToken(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        var start = index;
        while (index < text.Length && char.IsLetter(text[index]))
            index++;

        return index == start ? string.Empty : text[start..index].ToUpperInvariant();
    }
}

internal sealed class ProbeAdoNetParameter : DbParameter
{
    public override DbType DbType { get; set; }

    public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

    public override bool IsNullable { get; set; }

    [AllowNull]
    public override string ParameterName { get; set; } = string.Empty;

    [AllowNull]
    public override string SourceColumn { get; set; } = string.Empty;

    public override object? Value { get; set; }

    public override bool SourceColumnNullMapping { get; set; }

    public override int Size { get; set; }

    public override void ResetDbType()
    {
    }
}

internal sealed class ProbeAdoNetParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;

    public override object SyncRoot => ((ICollection)_parameters).SyncRoot;

    public override int Add(object value)
    {
        _parameters.Add((DbParameter)value);
        return _parameters.Count - 1;
    }

    public override void AddRange(Array values)
    {
        foreach (var value in values)
            Add(value!);
    }

    public override void Clear()
        => _parameters.Clear();

    public override bool Contains(object value)
        => value is DbParameter parameter && _parameters.Contains(parameter);

    public override bool Contains(string value)
        => IndexOf(value) >= 0;

    public override void CopyTo(Array array, int index)
        => ((ICollection)_parameters).CopyTo(array, index);

    public override IEnumerator GetEnumerator()
        => _parameters.GetEnumerator();

    public override int IndexOf(object value)
        => value is DbParameter parameter ? _parameters.IndexOf(parameter) : -1;

    public override int IndexOf(string parameterName)
        => _parameters.FindIndex(parameter => StringComparer.Ordinal.Equals(parameter.ParameterName, parameterName));

    public override void Insert(int index, object value)
        => _parameters.Insert(index, (DbParameter)value);

    public override void Remove(object value)
    {
        if (value is DbParameter parameter)
            _parameters.Remove(parameter);
    }

    public override void RemoveAt(int index)
        => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index >= 0)
            RemoveAt(index);
    }

    protected override DbParameter GetParameter(int index)
        => _parameters[index];

    protected override DbParameter GetParameter(string parameterName)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
            throw new IndexOutOfRangeException(parameterName);

        return _parameters[index];
    }

    protected override void SetParameter(int index, DbParameter value)
        => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value)
    {
        var index = IndexOf(parameterName);
        if (index < 0)
            _parameters.Add(value);
        else
            _parameters[index] = value;
    }
}

[JsonSerializable(typeof(AdoNetReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealAdoNetJsonContext : JsonSerializerContext;
