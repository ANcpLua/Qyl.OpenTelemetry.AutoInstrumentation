#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path
from typing import Any

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
PROJECT = ROOT / "demos" / "Qyl.RealHttpClientDemo" / "Qyl.RealHttpClientDemo.csproj"
GENERATOR_PROJECT = (
    ROOT
    / "src"
    / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators"
    / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
)
TARGET_FRAMEWORK = "net10.0"

# The one exclusion the demo's raw ActivityListener applies: the runtime's own socket / DNS / TLS /
# connection diagnostics. Any other scope has to appear in an asserted span set.
EXCLUDED_SCOPE_PREFIX = "Experimental."

# One HttpClient.GetAsync produces exactly one non-Experimental span: the BCL's own client span.
# The deleted qyl listener span reappearing changes this and fails the gate.
EXPECTED_SPAN_SETS = {
    "error-status": ["System.Net.Http/Client"],
    "connection-failure": ["System.Net.Http/Client"],
}


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

    for token in ["error-status=503", "expected-failure=HttpRequestException"]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report("HttpClient demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    verify_span_sets(name, report)

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 2:
        fail(f"{name} expected exactly 2 System.Net.Http activities, got {activities!r}")
    metrics = report.get("Metrics")
    if not isinstance(metrics, list) or len(metrics) != 2:
        fail(f"{name} expected exactly 2 HttpClient duration metrics, got {metrics!r}")


def run_nativeaot(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(GENERATOR_PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
            "-c",
            "Release",
            "-r",
            runtime_identifier("NativeAOT HttpClient gate"),
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
    executable = output / (
        "Qyl.RealHttpClientDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealHttpClientDemo"
    )
    if not executable.exists():
        fail(f"NativeAOT HttpClient executable missing: {executable}")

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
    verify_report("managed HttpClient demo", managed, "dynamic-code-supported")
    nativeaot = run_nativeaot(env)
    verify_report("NativeAOT HttpClient demo", nativeaot, "nativeaot")
    print("real-http-client-demo-ok")


if __name__ == "__main__":
    main()
