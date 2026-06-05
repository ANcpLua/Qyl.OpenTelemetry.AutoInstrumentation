using System.Data;
using System.Data.Common;
using System.Collections;
using System.Net;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Jobs;
using BenchmarkDotNet.Running;
using Qyl.AutoInstrumentation;

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
        using var httpClient = new HttpClientHotPathBenchmarks();
        await httpClient.InterceptedGetAsync();

        var dbCommand = new DbCommandHotPathBenchmarks();
        dbCommand.InterceptedSqlClientCommand();

        var efCore = new EntityFrameworkCoreHotPathBenchmarks();
        efCore.InterceptedExecuteSqlRaw();
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[SimpleJob(RuntimeMoniker.NativeAot10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class HttpClientHotPathBenchmarks : IDisposable
{
    private readonly HttpClient httpClient = new(new StaticHttpMessageHandler())
    {
        BaseAddress = new Uri("https://example.invalid"),
    };

    [Benchmark(Baseline = true)]
    public async Task<int> DirectGetAsync()
    {
        using var response = await httpClient.GetAsync("/", HttpCompletionOption.ResponseHeadersRead);
        return (int)response.StatusCode;
    }

    [Benchmark]
    public async Task<int> InterceptedGetAsync()
    {
        using var response = await QylInterceptedHttpClient.GetAsync(httpClient, "/");
        return (int)response.StatusCode;
    }

    public void Dispose() => httpClient.Dispose();
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
            QylAutoInstrumentationIds.SqlClient,
            "ExecuteScalar");

        return activity is null ? 0 : 1;
    }
}

[MemoryDiagnoser]
[SimpleJob(RuntimeMoniker.Net10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
[SimpleJob(RuntimeMoniker.NativeAot10_0, launchCount: 1, warmupCount: 3, iterationCount: 5)]
public class EntityFrameworkCoreHotPathBenchmarks
{
    private const string Statement = "INSERT INTO qyl_benchmark(value) VALUES (1)";

    [Benchmark(Baseline = true)]
    public int DirectExecuteSqlRaw() => Statement.Length;

    [Benchmark]
    public int InterceptedExecuteSqlRaw()
    {
        using var activity = QylInterceptedEntityFrameworkCore.StartActivity(Statement);

        return activity is null ? 0 : 1;
    }
}

internal sealed class StaticHttpMessageHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request,
        CancellationToken cancellationToken)
    {
        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            RequestMessage = request,
        });
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
