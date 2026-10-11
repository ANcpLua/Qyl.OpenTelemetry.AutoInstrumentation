#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
import sys
from pathlib import Path

from verify_helpers import artifacts_publish_dir, clean_env, fail, parse_report, run_checked, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealEfCoreDemo" / "Qyl.RealEfCoreDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if "expected-failure=SqliteException" not in completed.stdout:
        fail(f"{name} missing expected EFCore failure token\nstdout={completed.stdout}")

    report = parse_report("EFCore demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 3:
        fail(f"{name} expected exactly 3 EFCore activities, got {activities!r}")


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    if env.get("AOT_PUBLISH_GATE_SET") not in {"warned", "all"}:
        run_checked(
            [sys.executable, "tools/verify-aot-publish-gate.py", "--set", "warned", "--demo",
             PROJECT.stem, "--strict-promotion", "--keep-publish"], ROOT, env)
    executable = output / ("Qyl.RealEfCoreDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealEfCoreDemo")
    if not executable.exists():
        fail(f"NativeAOT EFCore executable missing: {executable}")

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
    verify_report("managed EFCore demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT EFCore demo", nativeaot, "nativeaot")

    # Opt-in statement capture: the demo requires db.query.text on the emitted
    # spans when SET_DBSTATEMENT_FOR_TEXT=true (and its default runs above
    # require the same tag to be ABSENT). Reuses the already-published NativeAOT
    # binary, so both runtime modes prove the option end-to-end.
    optin_env = dict(env)
    optin_env["OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT"] = "true"
    optin_env["AOT_PUBLISH_GATE_SET"] = env.get("AOT_PUBLISH_GATE_SET", "warned")
    verify_report("managed EFCore demo (statement opt-in)", run_managed(PROJECT, optin_env), "dynamic-code-supported")
    verify_report("NativeAOT EFCore demo (statement opt-in)", run_nativeaot(optin_env), "nativeaot")
    print("real-efcore-demo-ok")


if __name__ == "__main__":
    main()
