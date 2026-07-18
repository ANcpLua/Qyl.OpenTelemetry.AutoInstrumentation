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
CORE_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
EVENT_NAME = "qyl.http.client"


PROGRAM = r'''
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(activityListener);
EmitSyntheticHttpClientEvent();

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

[UnconditionalSuppressMessage("Trimming", "IL2026",
    Justification = "Synthetic offline producer for the NativeAOT verified gate; qyl runtime only consumes DiagnosticSource.")]
static void EmitSyntheticHttpClientEvent()
{
    using var diagnosticListener = new DiagnosticListener("HttpHandlerDiagnosticListener");
    if (!diagnosticListener.IsEnabled("qyl.http.client"))
    {
        Console.WriteLine("listener.enabled=false");
        return;
    }

    diagnosticListener.Write(
        "qyl.http.client",
        new Dictionary<string, object?>
        {
            ["http.request.method"] = "GET",
            ["url.full"] = "https://qyl.local/nativeaot/client?id=42",
            ["server.address"] = "qyl.local",
            ["http.response.status_code"] = 503,
            ["error.type"] = "503",
        });
}
'''


EXPECTED_VERIFIED = """name=GET
kind=Client
error.type=503
http.request.method=GET
http.response.status_code=503
qyl.instrumentation.domain=http.client
server.address=qyl.local
url.full=https://qyl.local/nativeaot/client?id=Redacted
"""


def fail(message: str) -> None:
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
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="{version}" />
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
