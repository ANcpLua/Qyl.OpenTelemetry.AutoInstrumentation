#!/usr/bin/env python3
"""Negative-space gate: a disabled instrumentation actually emits zero qyl telemetry.

The HttpClient source interceptor is present in the consumer; this gate fires a real request
with instrumentation off and proves its qyl span disappears. Native BCL metrics are
intentionally outside this gate because qyl controls their SDK registration, not whether the
framework records them. The logs signal declares no instrumentation, so
OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED is proved in tools/verify-real-ilogger-demo.py
against the log-record exporter Qyl.Telemetry.Hosting registers.

Control (enabled)  -> exactly one HttpClient span.
Disabled (http)    -> zero spans, via OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED=false.
Disabled (traces)  -> zero spans, via OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED=false.
Disabled (global)  -> zero spans, via OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED=false.
Disabled (other)   -> one span still, proving a per-integration switch stays selective.
"""
from __future__ import annotations

import subprocess
import tempfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked
from typing import NoReturn

ROOT = Path(__file__).resolve().parents[1]
CORE_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation" / "Qyl.Telemetry.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.Hosting" / "Qyl.Telemetry.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"

PROGRAM = r'''
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
'''


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    for project in [CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT]:
        run_checked(
            ["dotnet", "pack", str(project), "-c", "Release", "-o", str(feed), "-v", "quiet"],
            ROOT,
            env,
        )


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
    <PackageReference Include="Qyl.Telemetry.AutoInstrumentation" Version="{version}" />
    <PackageReference Include="Qyl.Telemetry.AutoInstrumentation.Hosting" Version="{version}" />
    <Compile Remove="Generated/**/*.cs" />
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
        fail(f"consumer run failed\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}")
    if completed.stderr:
        fail(f"consumer wrote stderr:\n{completed.stderr}")
    return completed.stdout.strip()


def assert_spans(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        fail(f"{name}: expected '{expected}', got '{actual}'")
    print(f"  {name}: {actual}")


def main() -> None:
    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-disabled-behavior-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
        assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"

        def expect(spans: int) -> str:
            return f"adonet.spans={spans}"

        assert_spans("enabled (control)", run_scenario(assembly, env, {}), expect(1))
        # Per-integration trace kill switch.
        assert_spans(
            "adonet trace instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_ADONET_INSTRUMENTATION_ENABLED": "false"}),
            expect(0),
        )
        # Signal-level kill switch.
        assert_spans(
            "traces signal disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED": "false"}),
            expect(0),
        )
        # Global kill switch.
        assert_spans(
            "global instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED": "false"}),
            expect(0),
        )
        # Selectivity: an unrelated integration's switch leaves the HttpClient span alone.
        assert_spans(
            "unrelated integration disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_SQLCLIENT_INSTRUMENTATION_ENABLED": "false"}),
            expect(1),
        )

    print("instrumentation-disabled-behavior-ok")


if __name__ == "__main__":
    main()
