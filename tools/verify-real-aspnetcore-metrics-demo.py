#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
import sys
from pathlib import Path

from verify_helpers import artifacts_publish_dir, clean_env, fail, parse_report, run_checked, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealAspNetCoreMetricsDemo" / "Qyl.RealAspNetCoreMetricsDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report("ASP.NET Core metrics demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    metrics = report.get("Metrics")
    if not isinstance(metrics, list) or len(metrics) < 3:
        fail(f"{name} expected at least 3 ASP.NET Core component metrics, got {metrics!r}")


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    if env.get("AOT_PUBLISH_GATE_SET") not in {"warned", "all"}:
        run_checked(
            [sys.executable, "tools/verify-aot-publish-gate.py", "--set", "warned", "--demo",
             PROJECT.stem, "--strict-promotion", "--keep-publish"], ROOT, env)
    executable = output / ("Qyl.RealAspNetCoreMetricsDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealAspNetCoreMetricsDemo")
    if not executable.exists():
        fail(f"NativeAOT ASP.NET Core metrics executable missing: {executable}")

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
    verify_report("managed ASP.NET Core metrics demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT ASP.NET Core metrics demo", nativeaot, "nativeaot")
    print("real-aspnetcore-metrics-demo-ok")


if __name__ == "__main__":
    main()
