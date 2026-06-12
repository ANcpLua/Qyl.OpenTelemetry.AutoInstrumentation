#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealGrpcClientDemo" / "Qyl.RealGrpcClientDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"gRPC client demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"gRPC client demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"gRPC client demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if "expected-failure=Unavailable" not in completed.stdout:
        fail(f"{name} missing expected gRPC failure token\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list):
        fail(f"{name} report Activities must be an array: {activities!r}")

    grpc_activities = [
        activity
        for activity in activities
        if isinstance(activity, dict)
        and isinstance(activity.get("Tags"), dict)
        and activity["Tags"].get("qyl.instrumentation.domain") == "rpc.grpc"
    ]
    if len(grpc_activities) != 2:
        fail(f"{name} expected exactly 2 gRPC client activities, got {grpc_activities!r}")


def run_managed(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    assembly = artifacts_bin_assembly(PROJECT)
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    managed = run_managed(env)
    verify_report("managed gRPC client demo", managed, "dynamic-code-supported")
    print("real-grpc-client-demo-ok")


if __name__ == "__main__":
    main()
