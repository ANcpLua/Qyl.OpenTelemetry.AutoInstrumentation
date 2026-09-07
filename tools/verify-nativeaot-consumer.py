#!/usr/bin/env python3
from __future__ import annotations

import platform
import subprocess
import tempfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked
from typing import NoReturn

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
CORE_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation" / "Qyl.Telemetry.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.Hosting" / "Qyl.Telemetry.AutoInstrumentation.Hosting.csproj"
SDK_PROJECT = ROOT / "src" / "Qyl.Telemetry.Hosting" / "Qyl.Telemetry.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
EVENT_NAME = "qyl.http.client"


PROGRAM = r'''
using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Qyl;

// The gate proves the qyl runtime still works inside a NativeAOT-published consumer. Until 15.0.0
// it did that by writing a synthetic event into the HttpClient DiagnosticListener, which qyl
// subscribed. That lane is gone — the DiagnosticListeners package no longer ships a single concrete
// subscriber — so the consumer now exercises the lane the release kept: System.Net.Http emits the
// span, and the processor AddQyl registers stamps the qyl domain onto it. The request goes to a
// closed loopback port, so the proof needs no server and stays deterministic.
var exported = new List<Activity>();
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-nativeaot-consumer";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
});
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));

using var host = builder.Build();
await host.StartAsync();

using (var http = new HttpClient())
{
    try
    {
        using var response = await http.GetAsync("http://127.0.0.1:1/nativeaot/client?id=42");
    }
    catch (HttpRequestException)
    {
        // The refused connection is the point: it produces a client span with an error, without a server.
    }
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000);
await host.StopAsync();

var captured = exported
    .Where(static activity =>
        activity.GetTagItem("qyl.instrumentation.domain") is "http.client")
    .ToList();

if (captured.Count != 1)
{
    Console.WriteLine("captured.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    return 1;
}

var activity = captured[0];
Console.WriteLine("name=" + activity.DisplayName);
Console.WriteLine("kind=" + activity.Kind);

foreach (var tag in activity.TagObjects.OrderBy(static tag => tag.Key, StringComparer.Ordinal))
{
    Console.WriteLine(tag.Key + "=" + Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
}

return 0;
'''


# The shape System.Net.Http emits with qyl's domain stamped on it: no status code because the
# connection was refused, error.type from the BCL rather than a status string, and the query
# redacted whole rather than per value.
EXPECTED_VERIFIED = """name=GET
kind=Client
error.type=connection_error
http.request.method=GET
qyl.instrumentation.domain=http.client
server.address=127.0.0.1
server.port=1
url.full=http://127.0.0.1:1/nativeaot/client?*
"""


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


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


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in [CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT, SDK_PROJECT]:
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
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.Telemetry.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Qyl.Telemetry.Hosting" Version="{version}" />
    <PackageReference Include="OpenTelemetry.Exporter.InMemory" Version="1.18.0" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def publish_nativeaot(project: Path, output: Path, env: dict[str, str]) -> Path:
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
        fail(f"NativeAOT executable missing: {executable}")

    return executable


def run_executable(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-nativeaot-verified-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        publish = root / "publish"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        executable = publish_nativeaot(project, publish, env)
        completed = run_executable(executable, env)

    if completed.returncode != 0:
        fail(
            "NativeAOT consumer failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"NativeAOT consumer wrote stderr:\n{completed.stderr}")
    if completed.stdout != EXPECTED_VERIFIED:
        fail(
            "NativeAOT verified mismatch\n"
            f"EXPECTED\n{EXPECTED_VERIFIED}\nACTUAL\n{completed.stdout}"
        )

    print("nativeaot-consumer-ok")


if __name__ == "__main__":
    main()
