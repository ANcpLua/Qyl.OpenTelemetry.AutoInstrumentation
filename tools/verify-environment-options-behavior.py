#!/usr/bin/env python3
from __future__ import annotations

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
CORE_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using Qyl.OpenTelemetry.AutoInstrumentation;

var options = QylAutoInstrumentationOptions.Current;

Console.WriteLine("global=" + options.GlobalEnabled);
Console.WriteLine("traces=" + options.TracesEnabled);
Console.WriteLine("metrics=" + options.MetricsEnabled);
Console.WriteLine("logs=" + options.LogsEnabled);
Console.WriteLine("trace.http=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient));
Console.WriteLine("trace.sql=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.SqlClient));
Console.WriteLine("metric.http=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient));
Console.WriteLine("metric.sql=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient));
Console.WriteLine("log.ilogger=" + options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, QylAutoInstrumentationIds.ILogger));
Console.WriteLine("meters=" + string.Join("|", QylMetricMeters.GetEnabledMeterNames()));
Console.WriteLine("ef.text=" + options.EntityFrameworkCoreSetDbStatementForText);
Console.WriteLine("graphql.document=" + options.GraphQlSetDocument);
Console.WriteLine("oracle.text=" + options.OracleMdaSetDbStatementForText);
Console.WriteLine("sql.text=" + options.SqlClientSetDbStatementForText);
Console.WriteLine("aspnetcore.req=" + string.Join("|", options.AspNetCoreCapturedRequestHeaders));
Console.WriteLine("aspnetcore.res=" + string.Join("|", options.AspNetCoreCapturedResponseHeaders));
Console.WriteLine("grpc.req=" + string.Join("|", options.GrpcNetClientCapturedRequestMetadata));
Console.WriteLine("grpc.res=" + string.Join("|", options.GrpcNetClientCapturedResponseMetadata));
Console.WriteLine("http.req=" + string.Join("|", options.HttpClientCapturedRequestHeaders));
Console.WriteLine("http.res=" + string.Join("|", options.HttpClientCapturedResponseHeaders));
Console.WriteLine("aspnetcore.query.unredacted=" + options.AspNetCoreUrlQueryRedactionDisabled);
Console.WriteLine("http.query.unredacted=" + options.HttpClientUrlQueryRedactionDisabled);
'''


# External-consumer runtime probe: public API only (no IVT — the assembly name is
# deliberately NOT VerifierProbe). Proves option env vars change EMITTED SPANS,
# not merely parsed option values: a real HttpClient call through the Hosting runtime
# listener against a loopback server, asserting url.full redaction on the stopped activity.
RUNTIME_PROGRAM = r'''
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(listener);

var tcp = new TcpListener(IPAddress.Loopback, 0);
tcp.Start(1);
var port = ((IPEndPoint)tcp.LocalEndpoint).Port;
var serve = Task.Run(async () =>
{
    using var client = await tcp.AcceptTcpClientAsync();
    await using var stream = client.GetStream();
    var buffer = new byte[8192];
    var received = new List<byte>();
    while (true)
    {
        var count = await stream.ReadAsync(buffer);
        if (count == 0) throw new InvalidOperationException("client closed early");
        received.AddRange(buffer.AsSpan(0, count).ToArray());
        if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0) break;
    }
    var response = Encoding.ASCII.GetBytes(
        "HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nX-Server: srv1\r\nConnection: close\r\n\r\n");
    await stream.WriteAsync(response);
});

using var http = new HttpClient();
http.DefaultRequestHeaders.Add("X-Client", "abc");
var response = await http.GetAsync($"http://127.0.0.1:{port}/probe?user=alice&token=hunter2");
await serve;
tcp.Stop();
Console.WriteLine("http.status=" + (int)response.StatusCode);

Console.WriteLine("activity.count=" + captured.Count);
foreach (var activity in captured)
{
    var tags = activity.TagObjects
        .Select(tag => (tag.Key, Value: tag.Value switch
        {
            string s => s,
            System.Collections.IEnumerable e => string.Join(",", e.Cast<object?>()),
            var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        }))
        .OrderBy(tag => tag.Key, StringComparer.Ordinal)
        .ToList();

    foreach (var (key, value) in tags)
    {
        if (key == "url.full")
            Console.WriteLine("url.full=" + value.Replace(":" + port, ":PORT"));
        else if (key.StartsWith("http.request.header.", StringComparison.Ordinal)
                 || key.StartsWith("http.response.header.", StringComparison.Ordinal))
            Console.WriteLine(key + "=" + value);
    }
}
'''


DEFAULT_EXPECTED = """global=True
traces=True
metrics=True
logs=True
trace.http=True
trace.sql=True
metric.http=True
metric.sql=True
log.ilogger=True
meters=Microsoft.AspNetCore.Hosting|Microsoft.AspNetCore.Routing|Microsoft.AspNetCore.Diagnostics|Microsoft.AspNetCore.RateLimiting|Microsoft.AspNetCore.HeaderParsing|Microsoft.AspNetCore.Server.Kestrel|Microsoft.AspNetCore.Http.Connections|Microsoft.AspNetCore.Authorization|Microsoft.AspNetCore.Authentication|Microsoft.AspNetCore.Components|Microsoft.AspNetCore.Components.Lifecycle|Microsoft.AspNetCore.Components.Server.Circuits|System.Net.Http|System.Net.NameResolution|Qyl.OpenTelemetry.AutoInstrumentation.Database|Qyl.OpenTelemetry.AutoInstrumentation.NServiceBus|System.Runtime
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
"""

GLOBAL_DISABLED_HTTP_TRACE_ENABLED_EXPECTED = """global=False
traces=False
metrics=False
logs=False
trace.http=True
trace.sql=False
metric.http=False
metric.sql=False
log.ilogger=False
meters=
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
"""

SIGNAL_AND_SPECIFIC_OVERRIDES_EXPECTED = """global=False
traces=True
metrics=True
logs=True
trace.http=True
trace.sql=False
metric.http=True
metric.sql=False
log.ilogger=False
meters=Microsoft.AspNetCore.Hosting|Microsoft.AspNetCore.Routing|Microsoft.AspNetCore.Diagnostics|Microsoft.AspNetCore.RateLimiting|Microsoft.AspNetCore.HeaderParsing|Microsoft.AspNetCore.Server.Kestrel|Microsoft.AspNetCore.Http.Connections|Microsoft.AspNetCore.Authorization|Microsoft.AspNetCore.Authentication|Microsoft.AspNetCore.Components|Microsoft.AspNetCore.Components.Lifecycle|Microsoft.AspNetCore.Components.Server.Circuits|System.Net.Http|System.Net.NameResolution|Qyl.OpenTelemetry.AutoInstrumentation.Database|Qyl.OpenTelemetry.AutoInstrumentation.NServiceBus|System.Runtime
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
"""

OPTIONS_EXPECTED = """global=True
traces=True
metrics=True
logs=True
trace.http=True
trace.sql=True
metric.http=True
metric.sql=True
log.ilogger=True
meters=Microsoft.AspNetCore.Hosting|Microsoft.AspNetCore.Routing|Microsoft.AspNetCore.Diagnostics|Microsoft.AspNetCore.RateLimiting|Microsoft.AspNetCore.HeaderParsing|Microsoft.AspNetCore.Server.Kestrel|Microsoft.AspNetCore.Http.Connections|Microsoft.AspNetCore.Authorization|Microsoft.AspNetCore.Authentication|Microsoft.AspNetCore.Components|Microsoft.AspNetCore.Components.Lifecycle|Microsoft.AspNetCore.Components.Server.Circuits|System.Net.Http|System.Net.NameResolution|Qyl.OpenTelemetry.AutoInstrumentation.Database|Qyl.OpenTelemetry.AutoInstrumentation.NServiceBus|System.Runtime
ef.text=True
graphql.document=True
oracle.text=True
sql.text=True
aspnetcore.req=x-core-request|tenant
aspnetcore.res=x-core-response|etag
grpc.req=traceparent|authorization
grpc.res=grpc-status|trailers
http.req=authorization|x-client
http.res=set-cookie|server
aspnetcore.query.unredacted=True
http.query.unredacted=True
"""

ADDITIONAL_METRIC_SOURCES_EXPECTED = """global=True
traces=True
metrics=True
logs=True
trace.http=True
trace.sql=True
metric.http=True
metric.sql=True
log.ilogger=True
meters=Microsoft.AspNetCore.Hosting|Microsoft.AspNetCore.Routing|Microsoft.AspNetCore.Diagnostics|Microsoft.AspNetCore.RateLimiting|Microsoft.AspNetCore.HeaderParsing|Microsoft.AspNetCore.Server.Kestrel|Microsoft.AspNetCore.Http.Connections|Microsoft.AspNetCore.Authorization|Microsoft.AspNetCore.Authentication|Microsoft.AspNetCore.Components|Microsoft.AspNetCore.Components.Lifecycle|Microsoft.AspNetCore.Components.Server.Circuits|System.Net.Http|System.Net.NameResolution|Qyl.OpenTelemetry.AutoInstrumentation.Database|Qyl.OpenTelemetry.AutoInstrumentation.NServiceBus|System.Runtime|YourCompany.CustomMeter|custom.case.Meter
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
"""


RUNTIME_DEFAULT_EXPECTED = """http.status=204
activity.count=1
url.full=http://127.0.0.1:PORT/probe?user=Redacted&token=Redacted
"""

RUNTIME_CAPTURE_EXPECTED = """http.status=204
activity.count=1
http.request.header.x-client=abc
http.response.header.x-server=srv1
url.full=http://127.0.0.1:PORT/probe?user=Redacted&token=Redacted
"""

RUNTIME_UNREDACTED_EXPECTED = """http.status=204
activity.count=1
url.full=http://127.0.0.1:PORT/probe?user=alice&token=hunter2
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in (CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT):
                run_checked(
                    ["dotnet", "pack", str(project), "-c", "Release", "-o", str(feed), "-v", "quiet"],
                    ROOT,
                    env,
                )
        finally:
            if fcntl is not None:
                fcntl.flock(lock, fcntl.LOCK_UN)


def write_project(
    directory: Path,
    feed: Path,
    packages: Path,
    version: str,
    assembly_name: str = "Qyl.OpenTelemetry.AutoInstrumentation.VerifierProbe",
    program: str | None = None,
    package_id: str = "Qyl.OpenTelemetry.AutoInstrumentation",
) -> Path:
    directory.mkdir(parents=True)
    project_path = directory / "Consumer.csproj"
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <AssemblyName>{assembly_name}</AssemblyName>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{feed};{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="{package_id}" Version="{version}" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(program if program is not None else PROGRAM, encoding="utf-8")
    return project_path


def run_scenario(assembly: Path, base_env: dict[str, str], overrides: dict[str, str]) -> str:
    env = dict(base_env)
    env.update(overrides)
    completed = subprocess.run(
        ["dotnet", str(assembly)],
        cwd=assembly.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(f"scenario failed\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}")
    if completed.stderr:
        fail(f"scenario wrote stderr:\n{completed.stderr}")

    return completed.stdout


def assert_scenario(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        fail(f"{name} mismatch\nEXPECTED\n{expected}\nACTUAL\n{actual}")


def main() -> None:
    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-env-options-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
        assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Qyl.OpenTelemetry.AutoInstrumentation.VerifierProbe.dll"

        assert_scenario("default", run_scenario(assembly, env, {}), DEFAULT_EXPECTED)
        assert_scenario(
            "global disabled with HTTP trace override",
            run_scenario(
                assembly,
                env,
                {
                    "OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED": "false",
                    "OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED": "true",
                },
            ),
            GLOBAL_DISABLED_HTTP_TRACE_ENABLED_EXPECTED,
        )
        assert_scenario(
            "signal and signal-specific overrides",
            run_scenario(
                assembly,
                env,
                {
                    "OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED": "false",
                    "OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED": "true",
                    "OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED": "true",
                    "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED": "true",
                    "OTEL_DOTNET_AUTO_TRACES_SQLCLIENT_INSTRUMENTATION_ENABLED": "false",
                    "OTEL_DOTNET_AUTO_METRICS_SQLCLIENT_INSTRUMENTATION_ENABLED": "false",
                    "OTEL_DOTNET_AUTO_LOGS_ILOGGER_INSTRUMENTATION_ENABLED": "false",
                },
            ),
            SIGNAL_AND_SPECIFIC_OVERRIDES_EXPECTED,
        )
        assert_scenario(
            "instrumentation options",
            run_scenario(
                assembly,
                env,
                {
                    "OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT": "true",
                    "OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT": "true",
                    "OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT": "true",
                    "OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT": "true",
                    "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "X-Core-Request,Tenant",
                    "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "X-Core-Response,ETag",
                    "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA": "TraceParent, Authorization",
                    "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA": "Grpc-Status, Trailers",
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "Authorization, X-Client",
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "Set-Cookie, Server",
                    "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION": "true",
                    "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION": "true",
                },
            ),
            OPTIONS_EXPECTED,
        )
        assert_scenario(
            "additional metric sources",
            run_scenario(
                assembly,
                env,
                {
                    "OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES": "YourCompany.CustomMeter, System.Net.Http, custom.case.Meter, YourCompany.CustomMeter",
                },
            ),
            ADDITIONAL_METRIC_SOURCES_EXPECTED,
        )

        runtime_project = write_project(
            root / "runtime-consumer",
            feed,
            packages,
            version,
            assembly_name="Qyl.OpenTelemetry.RuntimeProbe",
            program=RUNTIME_PROGRAM,
            package_id="Qyl.OpenTelemetry.AutoInstrumentation.Hosting",
        )
        run_checked(["dotnet", "build", str(runtime_project), "-c", "Release", "-v", "quiet"], runtime_project.parent, env)
        runtime_assembly = runtime_project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Qyl.OpenTelemetry.RuntimeProbe.dll"

        assert_scenario(
            "runtime: default redaction, no header capture",
            run_scenario(runtime_assembly, env, {}),
            RUNTIME_DEFAULT_EXPECTED,
        )
        assert_scenario(
            "runtime: captured headers on emitted span",
            run_scenario(
                runtime_assembly,
                env,
                {
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "X-Client",
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "X-Server",
                },
            ),
            RUNTIME_CAPTURE_EXPECTED,
        )
        assert_scenario(
            "runtime: url query redaction disabled",
            run_scenario(
                runtime_assembly,
                env,
                {"OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION": "true"},
            ),
            RUNTIME_UNREDACTED_EXPECTED,
        )

    print("environment-options-behavior-ok")


if __name__ == "__main__":
    main()
