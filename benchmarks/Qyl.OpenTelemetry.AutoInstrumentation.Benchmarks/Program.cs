using System.Data;
using System.Data.Common;
using System.Collections;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

if (args.Contains("--smoke", StringComparer.Ordinal))
{
    await BenchmarkSmoke.RunAsync();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

internal static class BenchmarkSmoke
{
    public static async Task RunAsync()
    {
        var dbCommand = new DbCommandHotPathBenchmarks();
        dbCommand.InterceptedSqlClientCommand();
        await Task.CompletedTask;
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[SimpleJob(RuntimeMoniker.NativeAot10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class DbCommandHotPathBenchmarks
{
    private readonly BenchmarkDbCommand command = new()
    {
        CommandText = "SELECT 1",
        CommandType = CommandType.Text,
    };

    [Benchmark(Baseline = true)]
    public int DirectSqlClientCommand() => command.CommandText!.Length;

    [Benchmark]
    public int InterceptedSqlClientCommand()
    {
        using var activity = QylInterceptedDbCommand.StartActivity(
            command,
            "SQLCLIENT",
            "ExecuteScalar");

        return activity is null ? 0 : 1;
    }
}

internal sealed class BenchmarkDbCommand : DbCommand
{
#pragma warning disable CS8765
    public override string CommandText { get; set; } = string.Empty;
#pragma warning restore CS8765

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; }

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection => EmptyDbParameterCollection.Instance;

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => 0;

    public override object ExecuteScalar() => 1;

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

internal sealed class EmptyDbParameterCollection : DbParameterCollection
{
    public static readonly EmptyDbParameterCollection Instance = new();

    private EmptyDbParameterCollection()
    {
    }

    public override int Count => 0;

    public override object SyncRoot => this;

    public override int Add(object value) => throw new NotSupportedException();

    public override void AddRange(Array values) => throw new NotSupportedException();

    public override void Clear()
    {
    }

    public override bool Contains(object value) => false;

    public override bool Contains(string value) => false;

    public override void CopyTo(Array array, int index)
    {
    }

    public override IEnumerator GetEnumerator() => Array.Empty<DbParameter>().GetEnumerator();

    public override int IndexOf(object value) => -1;

    public override int IndexOf(string parameterName) => -1;

    public override void Insert(int index, object value) => throw new NotSupportedException();

    public override void Remove(object value)
    {
    }

    public override void RemoveAt(int index)
    {
    }

    public override void RemoveAt(string parameterName)
    {
    }

    protected override DbParameter GetParameter(int index) => throw new ArgumentOutOfRangeException(nameof(index));

    protected override DbParameter GetParameter(string parameterName) => throw new ArgumentOutOfRangeException(nameof(parameterName));

    protected override void SetParameter(int index, DbParameter value) => throw new NotSupportedException();

    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
}
