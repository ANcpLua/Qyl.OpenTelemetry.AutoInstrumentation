#!/usr/bin/env python3
from __future__ import annotations

import platform
import subprocess
import tempfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners" / "Qyl.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.Hosting" / "Qyl.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Http;
using Microsoft.Extensions.Logging;
using Qyl.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(activityListener);

var dbMetrics = new List<CapturedDbMetric>();
using var meterListener = new MeterListener
{
    InstrumentPublished = static (instrument, listener) =>
    {
        if (instrument.Meter.Name == QylMetricMeters.DatabaseMeterName)
            listener.EnableMeasurementEvents(instrument);
    },
};
meterListener.SetMeasurementEventCallback<double>((instrument, measurement, tags, _) =>
{
    if (instrument.Name == QylMetricNames.DbClientOperationDuration)
        dbMetrics.Add(CapturedDbMetric.From(instrument, tags));
});
meterListener.Start();

var concreteLogger = new CapturingLogger();
ILogger logger = concreteLogger;
logger.Log(
    LogLevel.Warning,
    new EventId(7, "source-generated-log"),
    "source-generated-log",
    exception: null,
    static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);

Console.WriteLine("logger.calls=" + concreteLogger.Calls.ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("logger.last=" + concreteLogger.Last);
Console.WriteLine("activity.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

if (captured.Count == 1)
{
    var activity = captured[0];
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);
    tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain);
    tags.TryGetValue(QylSemanticAttributes.LogSeverity, out var severity);

    Console.WriteLine("activity.name=" + activity.DisplayName);
    Console.WriteLine("activity.kind=" + activity.Kind);
    Console.WriteLine(QylSemanticAttributes.QylInstrumentationDomain + "=" + domain);
    Console.WriteLine(QylSemanticAttributes.LogSeverity + "=" + severity);
}

using var http = new HttpClient(new StatusHandler(HttpStatusCode.InternalServerError))
{
    BaseAddress = new Uri("https://qyl-source.invalid"),
};
http.DefaultRequestHeaders.Add("x-qyl-source", "default-header");

try
{
    await http.GetStringAsync("/failure");
}
catch (HttpRequestException exception)
{
    var status = exception.StatusCode.HasValue
        ? ((int)exception.StatusCode.Value).ToString(System.Globalization.CultureInfo.InvariantCulture)
        : "<null>";
    Console.WriteLine("http.exception.status=" + status);
}

var httpActivity = captured.FirstOrDefault(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.QylInstrumentationDomain &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            QylInstrumentationDomains.HttpClient,
            StringComparison.Ordinal)));

if (httpActivity is not null)
{
    var tags = httpActivity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => FormatTagValue(tag.Value),
        StringComparer.Ordinal);
    tags.TryGetValue(QylSemanticAttributes.ServerAddress, out var serverAddress);
    tags.TryGetValue("http.request.header.x-qyl-source", out var capturedHeader);
    tags.TryGetValue(QylSemanticAttributes.HttpResponseStatusCode, out var statusCode);
    tags.TryGetValue(QylSemanticAttributes.ErrorType, out var errorType);

    Console.WriteLine("http.activity.status=" + httpActivity.Status);
    Console.WriteLine(QylSemanticAttributes.ServerAddress + "=" + serverAddress);
    Console.WriteLine("http.request.header.x-qyl-source=" + capturedHeader);
    Console.WriteLine(QylSemanticAttributes.HttpResponseStatusCode + "=" + statusCode);
    Console.WriteLine(QylSemanticAttributes.ErrorType + "=" + errorType);
}

using var content = new StringContent("payload");
content.Headers.Add("x-qyl-content", "content-header");
using var postHttp = new HttpClient(new StatusHandler(HttpStatusCode.NoContent))
{
    BaseAddress = new Uri("https://qyl-source.invalid"),
};
using (await postHttp.PostAsync("/content", content))
{
}

var postActivity = captured.FirstOrDefault(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.HttpResponseStatusCode &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            "500",
            StringComparison.Ordinal)) is false &&
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.QylInstrumentationDomain &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            QylInstrumentationDomains.HttpClient,
            StringComparison.Ordinal)));

if (postActivity is not null)
{
    var tags = postActivity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => FormatTagValue(tag.Value),
        StringComparer.Ordinal);
    tags.TryGetValue("http.request.header.x-qyl-content", out var capturedContentHeader);

    Console.WriteLine("http.request.header.x-qyl-content=" + capturedContentHeader);
}

using var sqlCommand = new Microsoft.Data.SqlClient.QylProbeSqlCommand("SELECT 1");
Console.WriteLine("db.scalar=" + Convert.ToString(sqlCommand.ExecuteScalar(), System.Globalization.CultureInfo.InvariantCulture));

try
{
    using var failingSqlCommand = new Microsoft.Data.SqlClient.QylProbeSqlCommand("SELECT FAIL");
    _ = failingSqlCommand.ExecuteScalar();
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("db.exception.type=" + exception.GetType().Name);
}

var dbActivity = captured.FirstOrDefault(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.DbSystemName &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            StringComparison.Ordinal)) &&
    activity.Status == ActivityStatusCode.Unset);

if (dbActivity is not null)
    Console.WriteLine("db.activity.status=" + dbActivity.Status);

var dbErrorActivity = captured.FirstOrDefault(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.DbSystemName &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            StringComparison.Ordinal)) &&
    activity.Status == ActivityStatusCode.Error);

if (dbErrorActivity is not null)
    Console.WriteLine("db.error.status=" + dbErrorActivity.Status);

var sqlClientMetric = dbMetrics.FirstOrDefault(static metric =>
    metric.Tags.TryGetValue(QylSemanticAttributes.DbSystemName, out var system) &&
    string.Equals(system, QylSemanticAttributes.DbSystemMicrosoftSqlServer, StringComparison.Ordinal));

Console.WriteLine("db.metric.count=" + dbMetrics.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
if (sqlClientMetric is not null)
{
    sqlClientMetric.Tags.TryGetValue(QylSemanticAttributes.DbSystemName, out var dbSystem);
    Console.WriteLine("db.metric.system=" + dbSystem);
}

ILogger throwingLogger = new ThrowingLogger();
try
{
    throwingLogger.Log(
        LogLevel.Error,
        new EventId(9, "source-generated-throw"),
        "source-generated-throw",
        exception: null,
        static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("throwing.type=" + exception.GetType().Name);
    Console.WriteLine("throwing.message=" + exception.Message);
}

Console.WriteLine("activity.count.after.throw=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

return 0;

static string FormatTagValue(object? value)
    => value is string[] values
        ? string.Join(",", values)
        : Convert.ToString(value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;

internal sealed record CapturedDbMetric(
    string Name,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedDbMetric From(Instrument instrument, ReadOnlySpan<KeyValuePair<string, object?>> tags)
    {
        var capturedTags = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var tag in tags)
        {
            capturedTags[tag.Key] = Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;
        }

        return new CapturedDbMetric(instrument.Name, capturedTags);
    }
}

internal sealed class CapturingLogger : ILogger
{
    public int Calls { get; private set; }

    public string Last { get; private set; } = string.Empty;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Calls++;
        Last = logLevel + ":" + eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + formatter(state, exception);
    }
}

internal sealed class StatusHandler(HttpStatusCode statusCode) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(statusCode)
        {
            RequestMessage = request,
            Content = new StringContent("failure"),
        });
}

internal sealed class ThrowingLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
        => throw new InvalidOperationException("logger-failure");
}

namespace Microsoft.Data.SqlClient
{
    internal sealed class QylProbeSqlCommand(string commandText) : DbCommand
    {
        private readonly QylProbeSqlParameterCollection _parameters = new();
        private DbConnection? _connection = new QylProbeSqlConnection();

        public override string CommandText { get; set; } = commandText;

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
            => 1;

        public override object? ExecuteScalar()
            => CommandText.Contains("FAIL", StringComparison.Ordinal)
                ? throw new InvalidOperationException("qyl-sqlclient-error")
                : 1;

        public override void Prepare()
        {
        }

        protected override DbParameter CreateDbParameter()
            => new QylProbeSqlParameter();

        protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior)
            => throw new NotSupportedException();
    }

    internal sealed class QylProbeSqlConnection : DbConnection
    {
        private ConnectionState _state = ConnectionState.Open;

        public override string ConnectionString { get; set; } = "Server=qyl-source-sqlclient;Database=qyl-source";

        public override string Database => "qyl-source";

        public override string DataSource => "qyl-source-sqlclient";

        public override string ServerVersion => "1.0";

        public override ConnectionState State => _state;

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
            => new QylProbeSqlCommand("SELECT 1");
    }

    internal sealed class QylProbeSqlParameter : DbParameter
    {
        public override DbType DbType { get; set; }

        public override ParameterDirection Direction { get; set; } = ParameterDirection.Input;

        public override bool IsNullable { get; set; }

        public override string ParameterName { get; set; } = string.Empty;

        public override string SourceColumn { get; set; } = string.Empty;

        public override object? Value { get; set; }

        public override bool SourceColumnNullMapping { get; set; }

        public override int Size { get; set; }

        public override void ResetDbType()
        {
        }
    }

    internal sealed class QylProbeSqlParameterCollection : DbParameterCollection
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
            => _parameters.ToArray().CopyTo(array, index);

        public override IEnumerator GetEnumerator()
            => _parameters.GetEnumerator();

        public override int IndexOf(object value)
            => value is DbParameter parameter ? _parameters.IndexOf(parameter) : -1;

        public override int IndexOf(string parameterName)
            => _parameters.FindIndex(parameter => string.Equals(parameter.ParameterName, parameterName, StringComparison.Ordinal));

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
            => _parameters[IndexOf(parameterName)];

        protected override void SetParameter(int index, DbParameter value)
            => _parameters[index] = value;

        protected override void SetParameter(string parameterName, DbParameter value)
        {
            var index = IndexOf(parameterName);
            if (index >= 0)
                _parameters[index] = value;
            else
                _parameters.Add(value);
        }
    }
}
'''


EXPECTED_VERIFIED = """logger.calls=1
logger.last=Warning:7:source-generated-log
activity.count=1
activity.name=ILogger log
activity.kind=Internal
qyl.instrumentation.domain=log.ilogger
log.severity=Warning
http.exception.status=500
http.activity.status=Error
server.address=qyl-source.invalid
http.request.header.x-qyl-source=default-header
http.response.status_code=500
error.type=500
http.request.header.x-qyl-content=content-header
db.scalar=1
db.exception.type=InvalidOperationException
db.activity.status=Unset
db.error.status=Error
db.metric.count=2
db.metric.system=microsoft.sql_server
throwing.type=InvalidOperationException
throwing.message=logger-failure
activity.count.after.throw=6
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in [CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT]:
                run_checked(
                    ["dotnet", "pack", str(project), "-c", "Release", "-o", str(feed), "-v", "quiet"],
                    ROOT,
                    env,
                )
        finally:
            if fcntl is not None:
                fcntl.flock(lock, fcntl.LOCK_UN)


def write_project(directory: Path, feed: Path, packages: Path, version: str) -> Path:
    directory.mkdir(parents=True)
    project_path = directory / "Consumer.csproj"
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{feed};{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.AutoInstrumentation" Version="{version}" />
    <PackageReference Include="Qyl.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"

    fail(f"unsupported NativeAOT gate platform: {platform.system()} {platform.machine()}")


def verify_generated_interceptor_source(directory: Path) -> None:
    generated_files = sorted((directory / "Generated").rglob("QylAutoInstrumentation.Interceptors.g.cs"))
    if len(generated_files) != 1:
        fail(f"expected exactly one generated interceptor source file, found {len(generated_files)}")

    text = generated_files[0].read_text(encoding="utf-8")
    for token in [
        "#nullable enable",
        "namespace Qyl.AutoInstrumentation.Generated",
        "[global::System.Runtime.CompilerServices.InterceptsLocationAttribute(",
        "// Intercepted call at ",
        "ILogger_Log_",
        "global::Qyl.AutoInstrumentation.QylInterceptedLogger.Log(",
        "HttpClient_GetStringAsync_",
        "global::Qyl.AutoInstrumentation.QylInterceptedHttpClient.GetStringAsync(",
        "HttpClient_PostAsync_",
        "global::Qyl.AutoInstrumentation.QylInterceptedHttpClient.PostAsync(",
        "DbCommand_ExecuteScalar_",
        "global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.GetTimestamp()",
        "global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.RecordDuration(",
        "global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.StartActivity(",
    ]:
        if token not in text:
            fail(f"generated interceptor source missing token: {token}")

    for token in [
        "QylActivitySource",
        ".SetTag(",
        "new global::System.Diagnostics.Activity",
    ]:
        if token in text:
            fail(f"generated interceptor source must not inline telemetry behavior: {token}")


def build_and_run(project: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
    verify_generated_interceptor_source(project.parent)
    assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=project.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def publish_nativeaot_and_run(project: Path, output: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(
        [
            "dotnet",
            "publish",
            str(project),
            "-c",
            "Release",
            "-r",
            runtime_identifier(),
            "-p:PublishAot=true",
            "--self-contained",
            "true",
            "-o",
            str(output),
            "-v",
            "quiet",
        ],
        project.parent,
        env,
    )
    executable = output / ("Consumer.exe" if platform.system().lower() == "windows" else "Consumer")
    if not executable.exists():
        fail(f"NativeAOT source interceptor executable missing: {executable}")

    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def verify_completed(name: str, completed: subprocess.CompletedProcess[str]) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")
    if completed.stdout != EXPECTED_VERIFIED:
        fail(
            f"{name} verified mismatch\n"
            f"EXPECTED\n{EXPECTED_VERIFIED}\nACTUAL\n{completed.stdout}"
        )


def main() -> None:
    env = clean_env()
    env["OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"] = "x-qyl-source,x-qyl-content"
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-source-interceptor-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        publish = root / "publish"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        managed = build_and_run(project, env)
        nativeaot = publish_nativeaot_and_run(project, publish, env)

    verify_completed("source interceptor managed consumer", managed)
    verify_completed("source interceptor NativeAOT consumer", nativeaot)

    print("source-interceptor-consumer-ok")


if __name__ == "__main__":
    main()
