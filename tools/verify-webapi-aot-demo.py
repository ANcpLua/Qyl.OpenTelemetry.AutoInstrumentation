#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import platform
import re
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
VERIFIED_PATH = ROOT / "tools" / "Qyl.OpenTelemetry.AutoInstrumentation.WebApiAotDemo" / "verified" / "report.json"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
TARGET_FRAMEWORK = "net10.0"

PROJECTS_TO_PACK = [
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore" / "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore.csproj",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient" / "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient.csproj",
]

EFCORE_COMPILED_MODEL_SOURCES = [
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "ProbeContext.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextAssemblyAttributes.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextModel.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextModelBuilder.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeItemEntityType.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeItemUnsafeAccessors.cs",
]

PROGRAM_TEMPLATE_PATH = ROOT / "tools" / "Qyl.OpenTelemetry.AutoInstrumentation.WebApiAotDemo" / "Program.cs"


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

    fail(f"unsupported NativeAOT web API gate platform: {platform.system()} {platform.machine()}")


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in PROJECTS_TO_PACK:
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
    project_path = directory / "WebApiAotDemo.csproj"
    compile_items = "\n".join(
        f'    <Compile Include="{path}" Link="EfCoreCompiledModel/{path.name}" />'
        for path in EFCORE_COMPILED_MODEL_SOURCES
    )
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{feed};{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <DefineConstants>$(DefineConstants);USE_COMPILED_MODEL</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore" Version="{version}" />
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.SqlClient" Version="{version}" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.1" />
  </ItemGroup>

  <ItemGroup>
{compile_items}
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    program = PROGRAM_TEMPLATE_PATH.read_text(encoding="utf-8")
    (directory / "Program.cs").write_text(program, encoding="utf-8")
    return project_path


def publish_nativeaot(project: Path, output: Path, log: Path, env: dict[str, str]) -> Path:
    completed = subprocess.run(
        [
            "dotnet",
            "publish",
            str(project),
            "-c",
            "Release",
            "-r",
            runtime_identifier(),
            "-p:PublishAot=true",
            "-p:TreatWarningsAsErrors=false",
            "--self-contained",
            "true",
            "-o",
            str(output),
            "-v",
            "quiet",
        ],
        cwd=project.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    log.write_text(completed.stdout, encoding="utf-8")
    if completed.returncode != 0:
        fail(
            "NativeAOT web API publish failed\n"
            f"exit={completed.returncode}\nlog={log}\n{completed.stdout}"
        )

    qyl_warnings = [
        line for line in completed.stdout.splitlines()
        if re.search(r"\b(?:IL2[0-9]{3}|IL3[0-9]{3}|IL4[0-9]{3}|CA[0-9]{4})\b", line) and
        "Qyl.OpenTelemetry.AutoInstrumentation" in line
    ]
    if qyl_warnings:
        fail("NativeAOT web API publish emitted qyl-owned analyzer warnings:\n" + "\n".join(qyl_warnings))

    executable = output / ("WebApiAotDemo.exe" if platform.system().lower() == "windows" else "WebApiAotDemo")
    if not executable.exists():
        fail(f"NativeAOT web API executable missing: {executable}")

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


def verify_or_update_verified(stdout: str, update: bool) -> None:
    parsed = json.loads(stdout)
    canonical = json.dumps(parsed, indent=2, sort_keys=True) + "\n"
    if update:
        VERIFIED_PATH.parent.mkdir(parents=True, exist_ok=True)
        VERIFIED_PATH.write_text(canonical, encoding="utf-8")
        return

    if not VERIFIED_PATH.exists():
        fail(f"missing web API AOT verified: {VERIFIED_PATH}")

    expected = VERIFIED_PATH.read_text(encoding="utf-8")
    if canonical != expected:
        received = VERIFIED_PATH.with_suffix(".received.json")
        received.write_text(canonical, encoding="utf-8")
        fail(
            "web API AOT verified mismatch\n"
            f"expected={VERIFIED_PATH}\n"
            f"received={received}"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description="Publish and run the NativeAOT web API instrumentation demo.")
    parser.add_argument("--update-verified", action="store_true", help="Update the committed canonical output.")
    args = parser.parse_args()

    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-webapi-aot-demo-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        publish = root / "publish"
        publish_log = root / "publish.log"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        executable = publish_nativeaot(project, publish, publish_log, env)
        completed = run_executable(executable, env)

    if completed.returncode != 0:
        fail(
            "NativeAOT web API demo failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"NativeAOT web API demo wrote stderr:\n{completed.stderr}")

    verify_or_update_verified(completed.stdout, args.update_verified)
    print("webapi-aot-demo-ok qyl_warnings=0")


if __name__ == "__main__":
    main()
