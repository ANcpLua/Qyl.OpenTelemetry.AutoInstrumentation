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
PROJECT = ROOT / "demos" / "Qyl.RealNetRuntimeMetricsDemo" / "Qyl.RealNetRuntimeMetricsDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report(".NET runtime metrics demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    metrics = report.get("Metrics")
    if not isinstance(metrics, list) or len(metrics) < 6:
        fail(f"{name} expected at least 6 .NET runtime/process metrics, got {metrics!r}")


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
            "-c",
            "Release",
            "-r",
            runtime_identifier("NativeAOT .NET runtime metrics gate"),
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
    executable = output / ("Qyl.RealNetRuntimeMetricsDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealNetRuntimeMetricsDemo")
    if not executable.exists():
        fail(f"NativeAOT .NET runtime metrics executable missing: {executable}")

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
    nativeaot = run_nativeaot(env)
    verify_report("managed .NET runtime metrics demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT .NET runtime metrics demo", nativeaot, "nativeaot")
    print("real-netruntime-metrics-demo-ok")


if __name__ == "__main__":
    main()
