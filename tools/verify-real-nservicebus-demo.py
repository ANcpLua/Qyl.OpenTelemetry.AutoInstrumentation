#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path

from verify_helpers import clean_env, fail, parse_report, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealNServiceBusDemo" / "Qyl.RealNServiceBusDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "published=alpha",
        "sent-and-handled=beta",
        "expected-nservicebus-error=no-route",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report("NServiceBus demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    # NServiceBus traces its own pipeline: the publish, the routed send, the send that cannot be
    # routed, the incoming "process message" span and the handler invocation beneath it. The last
    # two are the consumer side the interceptor never produced.
    if not isinstance(activities, list) or len(activities) != 5:
        fail(f"{name} expected exactly 5 NServiceBus activities, got {activities!r}")


def main() -> None:
    env = clean_env()
    managed = run_managed(PROJECT, env)
    verify_report("managed NServiceBus demo", managed, "dynamic-code-supported")
    print("real-nservicebus-demo-ok")


if __name__ == "__main__":
    main()
