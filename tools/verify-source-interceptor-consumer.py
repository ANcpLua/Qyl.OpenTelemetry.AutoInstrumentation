#!/usr/bin/env python3
from __future__ import annotations

import os
import platform
import subprocess
import tempfile
from pathlib import Path

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
PROPS_PATH = ROOT / "Directory.Build.props"
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners" / "Qyl.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.Hosting" / "Qyl.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Diagnostics;
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
'''


EXPECTED_GOLDEN = """logger.calls=1
logger.last=Warning:7:source-generated-log
activity.count=1
activity.name=ILogger log
activity.kind=Internal
qyl.instrumentation.domain=log.ilogger
log.severity=Warning
throwing.type=InvalidOperationException
throwing.message=logger-failure
activity.count.after.throw=2
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def clean_env() -> dict[str, str]:
    env = dict(os.environ)
    for key in list(env):
        if key.startswith("OTEL_") or key.startswith("QYL_"):
            del env[key]

    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["MSBUILDDISABLENODEREUSE"] = "1"
    return env


def read_version() -> str:
    text = PROPS_PATH.read_text(encoding="utf-8")
    prefix = "<Version>"
    suffix = "</Version>"
    start = text.find(prefix)
    if start < 0:
        fail("Directory.Build.props is missing <Version>")

    end = text.find(suffix, start)
    if end < 0:
        fail("Directory.Build.props has unterminated <Version>")

    return text[start + len(prefix):end].strip()


def run_checked(command: list[str], cwd: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        command,
        cwd=cwd,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(
            "command failed: "
            + " ".join(command)
            + f"\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )

    return completed


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
    if completed.stdout != EXPECTED_GOLDEN:
        fail(
            f"{name} golden mismatch\n"
            f"EXPECTED\n{EXPECTED_GOLDEN}\nACTUAL\n{completed.stdout}"
        )


def main() -> None:
    env = clean_env()
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
