#!/usr/bin/env python3
from __future__ import annotations

import os
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
using System;
using System.Threading.Tasks;

Console.WriteLine("stdout:begin");
Console.Error.WriteLine("stderr:begin");
Console.WriteLine("result:" + Compute(40, 2).ToString(System.Globalization.CultureInfo.InvariantCulture));

try
{
    ThrowExpected();
}
catch (InvalidOperationException exception)
{
    Console.WriteLine("caught:" + exception.GetType().Name + ":" + exception.Message);
}

Console.WriteLine("async:" + await EchoAsync("done"));
Console.WriteLine("stdout:end");
Console.Error.WriteLine("stderr:end");
return 7;

static int Compute(int left, int right) => left + right;

static void ThrowExpected() => throw new InvalidOperationException("expected");

static async Task<string> EchoAsync(string value)
{
    await Task.Yield();
    return value;
}
'''


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


def run_app(command: list[str], cwd: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=cwd,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def write_project(directory: Path, instrumented: bool, feed: Path, packages: Path, version: str) -> Path:
    directory.mkdir(parents=True)
    project_path = directory / "Consumer.csproj"
    package_reference = (
        f'''
  <ItemGroup>
    <PackageReference Include="Qyl.AutoInstrumentation.Hosting" Version="{version}" />
  </ItemGroup>
'''
        if instrumented
        else ""
    )
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
{package_reference}</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def build_and_run(project: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
    assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"
    return run_app(["dotnet", str(assembly)], project.parent, env)


def main() -> None:
    if not HOSTING_PROJECT.exists():
        fail(f"hosting project missing: {HOSTING_PROJECT}")

    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-consumer-behavior-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        pack_runtime(feed, env)
        baseline = build_and_run(
            write_project(root / "baseline", instrumented=False, feed=feed, packages=packages, version=version),
            env,
        )
        instrumented = build_and_run(
            write_project(root / "instrumented", instrumented=True, feed=feed, packages=packages, version=version),
            env,
        )

    mismatches: list[str] = []
    if baseline.returncode != instrumented.returncode:
        mismatches.append(f"exit code: baseline={baseline.returncode} instrumented={instrumented.returncode}")
    if baseline.stdout != instrumented.stdout:
        mismatches.append(f"stdout:\nBASELINE\n{baseline.stdout}\nINSTRUMENTED\n{instrumented.stdout}")
    if baseline.stderr != instrumented.stderr:
        mismatches.append(f"stderr:\nBASELINE\n{baseline.stderr}\nINSTRUMENTED\n{instrumented.stderr}")

    if mismatches:
        fail("consumer behavior changed with Qyl.AutoInstrumentation.Hosting reference:\n" + "\n".join(mismatches))

    print("consumer-behavior-ok")


if __name__ == "__main__":
    main()
