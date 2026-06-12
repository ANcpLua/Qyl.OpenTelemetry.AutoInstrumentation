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
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using Qyl.AutoInstrumentation;

var options = QylAutoInstrumentationOptions.Current;

Console.WriteLine("global=" + options.GlobalEnabled);
Console.WriteLine("traces=" + options.TracesEnabled);
Console.WriteLine("metrics=" + options.MetricsEnabled);
Console.WriteLine("logs=" + options.LogsEnabled);
Console.WriteLine("conformance=" + options.ConformanceProcessorEnabled);
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
Console.WriteLine("aspnet.req=" + string.Join("|", options.AspNetCapturedRequestHeaders));
Console.WriteLine("aspnet.res=" + string.Join("|", options.AspNetCapturedResponseHeaders));
Console.WriteLine("aspnetcore.req=" + string.Join("|", options.AspNetCoreCapturedRequestHeaders));
Console.WriteLine("aspnetcore.res=" + string.Join("|", options.AspNetCoreCapturedResponseHeaders));
Console.WriteLine("grpc.req=" + string.Join("|", options.GrpcNetClientCapturedRequestMetadata));
Console.WriteLine("grpc.res=" + string.Join("|", options.GrpcNetClientCapturedResponseMetadata));
Console.WriteLine("http.req=" + string.Join("|", options.HttpClientCapturedRequestHeaders));
Console.WriteLine("http.res=" + string.Join("|", options.HttpClientCapturedResponseHeaders));
Console.WriteLine("aspnetcore.query.unredacted=" + options.AspNetCoreUrlQueryRedactionDisabled);
Console.WriteLine("http.query.unredacted=" + options.HttpClientUrlQueryRedactionDisabled);
Console.WriteLine("aspnet.query.unredacted=" + options.AspNetUrlQueryRedactionDisabled);
Console.WriteLine("sql.ilrewrite.requested=" + options.SqlClientNetFxIlRewriteRequested);
Console.WriteLine("sql.ilrewrite.enabled=" + options.SqlClientNetFxIlRewriteEnabled);
'''


DEFAULT_EXPECTED = """global=True
traces=True
metrics=True
logs=True
conformance=False
trace.http=True
trace.sql=True
metric.http=True
metric.sql=True
log.ilogger=True
meters=Microsoft.AspNetCore.Components|System.Net.Http|Npgsql|Qyl.AutoInstrumentation.Database|NServiceBus.Core|System.Runtime
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnet.req=
aspnet.res=
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
aspnet.query.unredacted=False
sql.ilrewrite.requested=False
sql.ilrewrite.enabled=False
"""

GLOBAL_DISABLED_HTTP_TRACE_ENABLED_EXPECTED = """global=False
traces=False
metrics=False
logs=False
conformance=False
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
aspnet.req=
aspnet.res=
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
aspnet.query.unredacted=False
sql.ilrewrite.requested=False
sql.ilrewrite.enabled=False
"""

SIGNAL_AND_SPECIFIC_OVERRIDES_EXPECTED = """global=False
traces=True
metrics=True
logs=True
conformance=False
trace.http=True
trace.sql=False
metric.http=True
metric.sql=False
log.ilogger=False
meters=Microsoft.AspNetCore.Components|System.Net.Http|Npgsql|Qyl.AutoInstrumentation.Database|NServiceBus.Core|System.Runtime
ef.text=False
graphql.document=False
oracle.text=False
sql.text=False
aspnet.req=
aspnet.res=
aspnetcore.req=
aspnetcore.res=
grpc.req=
grpc.res=
http.req=
http.res=
aspnetcore.query.unredacted=False
http.query.unredacted=False
aspnet.query.unredacted=False
sql.ilrewrite.requested=False
sql.ilrewrite.enabled=False
"""

OPTIONS_EXPECTED = """global=True
traces=True
metrics=True
logs=True
conformance=True
trace.http=True
trace.sql=True
metric.http=True
metric.sql=True
log.ilogger=True
meters=Microsoft.AspNetCore.Components|System.Net.Http|Npgsql|Qyl.AutoInstrumentation.Database|NServiceBus.Core|System.Runtime
ef.text=True
graphql.document=True
oracle.text=True
sql.text=True
aspnet.req=x-request|user
aspnet.res=x-response|cache
aspnetcore.req=x-core-request|tenant
aspnetcore.res=x-core-response|etag
grpc.req=traceparent|authorization
grpc.res=grpc-status|trailers
http.req=authorization|x-client
http.res=set-cookie|server
aspnetcore.query.unredacted=True
http.query.unredacted=True
aspnet.query.unredacted=True
sql.ilrewrite.requested=True
sql.ilrewrite.enabled=False
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            run_checked(
                ["dotnet", "pack", str(CORE_PROJECT), "-c", "Release", "-o", str(feed), "-v", "quiet"],
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
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.AutoInstrumentation" Version="{version}" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
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
        assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"

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
                    "OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "X-Request, User, x-request",
                    "OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "X-Response, Cache",
                    "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "X-Core-Request,Tenant",
                    "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "X-Core-Response,ETag",
                    "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA": "TraceParent, Authorization",
                    "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA": "Grpc-Status, Trailers",
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS": "Authorization, X-Client",
                    "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS": "Set-Cookie, Server",
                    "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION": "true",
                    "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION": "true",
                    "OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION": "true",
                    "OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED": "true",
                    "QYL_CONFORMANCE_ENABLED": "true",
                },
            ),
            OPTIONS_EXPECTED,
        )

    print("environment-options-behavior-ok")


if __name__ == "__main__":
    main()
