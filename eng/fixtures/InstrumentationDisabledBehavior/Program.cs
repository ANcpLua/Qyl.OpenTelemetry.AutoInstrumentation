using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Qyl.Telemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(listener);

// The ADO.NET DbCommand lane is the interceptor the toggles gate after 15.0.0: System.Data.Common
// declares no ActivitySource, so the qyl span is the only span, and its absence is the kill switch
// working rather than a library falling silent. The probe implements the abstract command itself,
// so no database and no provider package is involved.
using var command = new ProbeCommand();
_ = command.ExecuteScalar();

var databaseSpans = captured.Count(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == "qyl.instrumentation.domain" &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            "db.client",
            StringComparison.Ordinal)));

Console.WriteLine("adonet.spans=" + databaseSpans.ToString(System.Globalization.CultureInfo.InvariantCulture));
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
