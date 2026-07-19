#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
import sys
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealOracleMdaDemo" / "Qyl.RealOracleMdaDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"Oracle MDA demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"Oracle MDA demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"Oracle MDA demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if completed.stdout.count("expected-oraclemda-error=InvalidOperationException") != 2:
        fail(f"{name} expected exactly 2 Oracle MDA error tokens\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 2:
        fail(f"{name} expected exactly 2 Oracle MDA activities, got {activities!r}")


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


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    if env.get("AOT_PUBLISH_GATE_SET") not in {"warned", "all"}:
        run_checked(
            [sys.executable, "tools/verify-aot-publish-gate.py", "--set", "warned", "--demo",
             PROJECT.stem, "--strict-promotion", "--keep-publish"], ROOT, env)
    executable = output / ("Qyl.RealOracleMdaDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealOracleMdaDemo")
    if not executable.exists():
        fail(f"NativeAOT Oracle MDA executable missing: {executable}")

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
    managed = run_managed(env)
    nativeaot = run_nativeaot(env)
    verify_report("managed Oracle MDA demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT Oracle MDA demo", nativeaot, "nativeaot")
    # Statement opt-in: the failed SELECT spans must carry db.query.text when
    # OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT=true (and never by default,
    # asserted by the runs above).
    optin_env = dict(env)
    optin_env["OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT"] = "true"
    optin_env["AOT_PUBLISH_GATE_SET"] = env.get("AOT_PUBLISH_GATE_SET", "warned")
    verify_report("managed Oracle MDA demo (statement opt-in)", run_managed(optin_env), "dynamic-code-supported")
    verify_report("NativeAOT Oracle MDA demo (statement opt-in)", run_nativeaot(optin_env), "nativeaot")
    print("real-oraclemda-demo-ok")


if __name__ == "__main__":
    main()
