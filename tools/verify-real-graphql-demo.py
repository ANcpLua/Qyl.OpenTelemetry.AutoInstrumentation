#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
import sys
from pathlib import Path
from typing import Any, NoReturn

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealGraphQlDemo" / "Qyl.RealGraphQlDemo.csproj"
TARGET_FRAMEWORK = "net10.0"

# The one exclusion the demo's raw ActivityListener applies: the runtime's own socket / DNS / TLS /
# connection diagnostics. Any other scope has to appear in an asserted span set.
EXCLUDED_SCOPE_PREFIX = "Experimental."

# GraphQL.NET's ActivitySource is silent until the application calls UseTelemetry() itself. The
# opted-in executer produces exactly one span; the identical executer without the opt-in produces
# none, and qyl's AddSource cannot change that.
EXPECTED_SPAN_SETS = {
    "query-with-telemetry": ["GraphQL/Internal"],
    "query-without-telemetry": [],
}


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"GraphQL demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"GraphQL demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"GraphQL demo report must be a JSON object: {report!r}")
    return report


def verify_span_sets(name: str, report: dict[str, Any]) -> None:
    span_sets = report.get("SpanSets")
    if not isinstance(span_sets, list) or len(span_sets) != len(EXPECTED_SPAN_SETS):
        fail(f"{name} expected {len(EXPECTED_SPAN_SETS)} operation span sets, got {span_sets!r}")

    for span_set in span_sets:
        operation = span_set.get("Operation")
        if operation not in EXPECTED_SPAN_SETS:
            fail(f"{name} unexpected operation {operation!r} in span sets")

        expected = EXPECTED_SPAN_SETS[operation]
        # The demo asserts the exact sorted multiset itself; the verifier pins the expected side so
        # a demo that quietly relaxes its own expectation still fails here.
        if span_set.get("Expected") != expected:
            fail(f"{name} operation {operation} expected span set drifted: {span_set.get('Expected')!r} != {expected!r}")
        if span_set.get("Actual") != expected:
            fail(f"{name} operation {operation} span set mismatch: {span_set.get('Actual')!r} != {expected!r}")

    excluded = report.get("ExcludedScopes")
    if not isinstance(excluded, list):
        fail(f"{name} report is missing ExcludedScopes: {excluded!r}")
    for scope in excluded:
        if not isinstance(scope, str) or not scope.startswith(EXCLUDED_SCOPE_PREFIX):
            fail(f"{name} excluded a scope outside the {EXCLUDED_SCOPE_PREFIX!r} prefix: {scope!r}")


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in ["graphql-with-telemetry-success=true", "graphql-without-telemetry-success=true"]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    verify_span_sets(name, report)

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 1:
        fail(f"{name} expected exactly 1 GraphQL activity, got {activities!r}")


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
    executable = output / ("Qyl.RealGraphQlDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealGraphQlDemo")
    if not executable.exists():
        fail(f"NativeAOT GraphQL executable missing: {executable}")

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
    verify_report("managed GraphQL demo", managed, "dynamic-code-supported")
    nativeaot = run_nativeaot(env)
    verify_report("NativeAOT GraphQL demo", nativeaot, "nativeaot")
    print("real-graphql-demo-ok")


if __name__ == "__main__":
    main()
