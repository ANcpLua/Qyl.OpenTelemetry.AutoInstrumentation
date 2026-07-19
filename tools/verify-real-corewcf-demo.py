#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, clean_env, run_checked


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealCoreWcfDemo" / "Qyl.RealCoreWcfDemo.csproj"


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"CoreWCF demo did not emit a JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"CoreWCF demo emitted invalid JSON: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"CoreWCF report must be an object: {report!r}")
    return report


def run_demo(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["dotnet", str(artifacts_bin_assembly(PROJECT))],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def verify_demo(completed: subprocess.CompletedProcess[str], *, registration_enabled: bool) -> None:
    if completed.returncode != 0:
        fail(
            "managed CoreWCF demo failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"managed CoreWCF demo wrote stderr:\n{completed.stderr}")

    report = parse_report(completed.stdout)
    if report.get("Pass") is not True:
        fail(f"CoreWCF report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    if report.get("QylRegistrationEnabled") is not registration_enabled:
        fail(f"CoreWCF registration state mismatch: {report!r}")
    if report.get("QylRegistrationObserved") is not registration_enabled:
        fail(f"CoreWCF source subscription mismatch: {report!r}")

    activities = report.get("Activities")
    expected_count = 2 if registration_enabled else 0
    if not isinstance(activities, list) or len(activities) != expected_count:
        fail(f"expected exactly {expected_count} CoreWCF activities, got {activities!r}")


def main() -> None:
    env = clean_env()
    run_checked(
        [
            "dotnet",
            "build",
            str(PROJECT),
            "-c",
            "Release",
            "-v",
            "quiet",
            "--disable-build-servers",
            "-m:1",
        ],
        ROOT,
        env,
    )
    verify_demo(run_demo(env), registration_enabled=True)

    disabled_env = dict(env)
    disabled_env["OTEL_DOTNET_AUTO_TRACES_WCFCORE_INSTRUMENTATION_ENABLED"] = "false"
    verify_demo(run_demo(disabled_env), registration_enabled=False)

    print("real-corewcf-demo-ok managed-only")


if __name__ == "__main__":
    main()
