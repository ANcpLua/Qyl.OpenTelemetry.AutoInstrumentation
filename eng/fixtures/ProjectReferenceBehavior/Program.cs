using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(activityListener);

using var command = new ProbeCommand();
_ = command.ExecuteScalar();

Console.WriteLine("command.calls=" + command.Calls.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.count=" + captured.Count.ToString(CultureInfo.InvariantCulture));

if (captured.Count == 1)
{
    var activity = captured[0];
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);
    tags.TryGetValue("qyl.instrumentation.domain", out var domain);
    tags.TryGetValue("db.system.name", out var system);
    tags.TryGetValue("db.operation.name", out var operation);
    tags.TryGetValue("db.query.summary", out var summary);
    tags.TryGetValue("db.query.text", out var queryText);

    Console.WriteLine("activity.name=" + activity.DisplayName);
    Console.WriteLine("activity.kind=" + activity.Kind);
    Console.WriteLine("qyl.instrumentation.domain=" + domain);
    Console.WriteLine("db.system.name=" + system);
    Console.WriteLine("db.operation.name=" + operation);
    Console.WriteLine("db.query.summary=" + summary);
    Console.WriteLine("db.query.text=" + (queryText ?? "<absent>"));
}

return 0;

internal sealed class ProbeCommand : DbCommand
{
    public int Calls { get; private set; }

    [AllowNull]
    public override string CommandText { get; set; } = "SELECT 1";

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection { get; } = new ProbeParameterCollection();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => 0;

    public override object? ExecuteScalar()
    {
        Calls++;
        return null;
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

internal sealed class ProbeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;

    public override object SyncRoot { get; } = new();

    public override int Add(object value) => throw new NotSupportedException();

    public override void AddRange(Array values) => throw new NotSupportedException();

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => false;

    public override bool Contains(string value) => false;

    public override void CopyTo(Array array, int index) => throw new NotSupportedException();

    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => -1;

    public override int IndexOf(string parameterName) => -1;

    public override void Insert(int index, object value) => throw new NotSupportedException();

    public override void Remove(object value) => throw new NotSupportedException();

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) => throw new NotSupportedException();

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
}
