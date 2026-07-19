#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealAspNetCoreDemo" / "Qyl.RealAspNetCoreDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


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

    fail(f"unsupported NativeAOT ASP.NET Core gate platform: {platform.system()} {platform.machine()}")


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"ASP.NET Core demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"ASP.NET Core demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"ASP.NET Core demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) < 2:
        fail(f"{name} expected at least 2 ASP.NET Core activities, got {activities!r}")


def build_managed(env: dict[str, str]) -> Path:
    run_checked(
        ["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet", "--no-incremental"],
        ROOT,
        env,
    )
    return artifacts_bin_assembly(PROJECT)


def run_managed(assembly: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def publish_nativeaot(env: dict[str, str]) -> Path:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
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
        ROOT,
        env,
    )
    executable = output / ("Qyl.RealAspNetCoreDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealAspNetCoreDemo")
    if not executable.exists():
        fail(f"NativeAOT ASP.NET Core executable missing: {executable}")

    return executable


def run_nativeaot(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
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
    optin_env = dict(env)
    optin_env["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"] = "X-Demo-Req"
    optin_env["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"] = "X-Demo-Res"
    optin_env["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"] = "true"
    optin_env["AOT_PUBLISH_GATE_SET"] = env.get("AOT_PUBLISH_GATE_SET", "warned")

    managed_assembly = build_managed(env)
    managed = run_managed(managed_assembly, env)
    managed_optin = run_managed(managed_assembly, optin_env)
    nativeaot_executable = publish_nativeaot(env)
    nativeaot = run_nativeaot(nativeaot_executable, env)
    nativeaot_optin = run_nativeaot(nativeaot_executable, optin_env)

    verify_report("managed ASP.NET Core demo", managed, "dynamic-code-supported")
    verify_report("managed ASP.NET Core demo (capture opt-in)", managed_optin, "dynamic-code-supported")
    verify_report("NativeAOT ASP.NET Core demo", nativeaot, "nativeaot")
    verify_report("NativeAOT ASP.NET Core demo (capture opt-in)", nativeaot_optin, "nativeaot")
    print("real-aspnetcore-demo-ok")


if __name__ == "__main__":
    main()
