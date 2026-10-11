#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any, NoReturn

from verify_helpers import artifacts_bin_assembly, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealQuartzDemo" / "Qyl.RealQuartzDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"Quartz demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"Quartz demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"Quartz demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str, metrics_enabled: bool) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "scheduler-fired=true",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    # One Quartz.Job.Execute span per scheduled job. The scheduler traces its job-store calls on
    # the same source; the demo reports only the firings.
    if not isinstance(activities, list) or len(activities) != 2:
        fail(f"{name} expected exactly 2 Quartz job-execute activities, got {activities!r}")
    if report.get("ExpectedMetricsEnabled") is not metrics_enabled:
        fail(f"{name} metrics expectation mismatch: {report.get('ExpectedMetricsEnabled')!r}")
    job_metrics = report.get("JobMetrics")
    expected_series = 2 if metrics_enabled else 0
    if not isinstance(job_metrics, list) or len(job_metrics) != expected_series:
        fail(f"{name} expected {expected_series} job duration series, got {job_metrics!r}")


def run_managed(env: dict[str, str], metrics_enabled: bool) -> subprocess.CompletedProcess[str]:
    assembly = artifacts_bin_assembly(PROJECT)
    return subprocess.run(
        ["dotnet", str(assembly)] + ([] if metrics_enabled else ["--expect-no-quartz-metrics"]),
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    cases = [
        ("default", {}, True),
        ("Quartz metrics disabled", {"OTEL_DOTNET_AUTO_METRICS_QUARTZ_INSTRUMENTATION_ENABLED": "false"}, False),
        ("all metrics disabled", {"OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED": "false"}, False),
        ("Quartz metrics override", {
            "OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED": "false",
            "OTEL_DOTNET_AUTO_METRICS_QUARTZ_INSTRUMENTATION_ENABLED": "true",
        }, True),
    ]
    for name, overrides, metrics_enabled in cases:
        verify_report(name, run_managed(env | overrides, metrics_enabled), "dynamic-code-supported", metrics_enabled)
        print(f"  - {name}: traces=2 job-duration-series={2 if metrics_enabled else 0}")
    print("real-quartz-demo-ok")


if __name__ == "__main__":
    main()
