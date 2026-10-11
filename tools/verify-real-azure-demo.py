#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path

from verify_helpers import (
    artifacts_publish_dir,
    clean_env,
    fail,
    parse_report,
    run_checked,
    run_managed,
    runtime_identifier,
)

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealAzureDemo" / "Qyl.RealAzureDemo.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if completed.stdout.count("expected-azure-error=RequestFailedException") != 2:
        fail(f"{name} expected exactly 2 Azure error tokens\nstdout={completed.stdout}")

    report = parse_report("Azure demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 4:
        fail(f"{name} expected exactly 4 Azure activities, got {activities!r}")


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(GENERATOR_PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
            "-c",
            "Release",
            "-r",
            runtime_identifier("NativeAOT Azure gate"),
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
    executable = output / ("Qyl.RealAzureDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealAzureDemo")
    if not executable.exists():
        fail(f"NativeAOT Azure executable missing: {executable}")

    return subprocess.run(
        [str(executable)],
        cwd=output,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    managed = run_managed(PROJECT, env)
    verify_report("managed Azure demo", managed, "dynamic-code-supported")
    nativeaot = run_nativeaot(env)
    verify_report("NativeAOT Azure demo", nativeaot, "nativeaot")
    print("real-azure-demo-ok")


if __name__ == "__main__":
    main()
