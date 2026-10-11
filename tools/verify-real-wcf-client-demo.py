#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path

from verify_helpers import clean_env, fail, parse_report, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealWcfClientDemo" / "Qyl.RealWcfClientDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if completed.stdout.count("expected-wcf-error=CommunicationException") != 2:
        fail(f"{name} expected exactly 2 WCF error tokens\nstdout={completed.stdout}")

    report = parse_report("WCF client demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 2:
        fail(f"{name} expected exactly 2 WCF client activities, got {activities!r}")


def main() -> None:
    env = clean_env()
    managed = run_managed(PROJECT, env)
    verify_report("managed WCF client demo", managed, "dynamic-code-supported")
    print("real-wcf-client-demo-ok")


if __name__ == "__main__":
    main()
