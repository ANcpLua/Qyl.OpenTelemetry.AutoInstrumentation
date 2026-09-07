#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import platform
import subprocess
from pathlib import Path
from typing import Any, NoReturn

from verify_container_helpers import run_published_container
from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealMySqlConnectorDemo" / "Qyl.RealMySqlConnectorDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
MYSQL_IMAGE = os.environ.get("QYL_MYSQL_IMAGE", "mysql:9")
DATABASE = "qyl"

# MySqlConnector picks its attribute flavour from this variable, and the README documents qyl
# against the stable one. The demo sets it too, so neither side can silently drift.
STABILITY_OPT_IN = "database"

# The exact sorted multiset of (ActivitySource.Name, Activity.Kind) tuples each operation must
# produce -- pinned here as well as in the demo, so shrinking the demo's own expectation cannot
# quietly shrink the evidence. Unlike Npgsql, MySqlConnector traces EVERY Open, pooled or not;
# closing the connection is what it does not trace.
EXPECTED_WINDOWS = {
    "connection-open": ["MySqlConnector|Client"],
    "select": ["MySqlConnector|Client"],
    "failing-select": ["MySqlConnector|Client"],
    "connection-close": [],
    "pooled-reopen": ["MySqlConnector|Client"],
}

# The runtime's own socket / DNS / TLS diagnostics are the only spans the demo drops. Any other
# source -- an OpenTelemetry.Instrumentation.* package, or the deleted qyl DbCommand interceptor --
# stays in the asserted multiset and fails the gate.
EXPECTED_EXCLUDED_PREFIXES = ["Experimental."]


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"

    fail(f"unsupported NativeAOT MySqlConnector gate platform: {platform.system()} {platform.machine()}")


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"MySqlConnector demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"MySqlConnector demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"MySqlConnector demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "connection-state=Open",
        "select-scalar=1",
        # ER_BAD_FIELD_ERROR, which the failed span carries in error.type and
        # db.response.status_code -- but never as an exception event.
        "expected-mysql-error-number=1054",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    if report.get("ExcludedSourcePrefixes") != EXPECTED_EXCLUDED_PREFIXES:
        fail(
            f"{name} excluded prefixes changed: expected={EXPECTED_EXCLUDED_PREFIXES} "
            f"actual={report.get('ExcludedSourcePrefixes')!r}"
        )

    operations = report.get("Operations")
    if not isinstance(operations, list):
        fail(f"{name} report has no Operations list: {operations!r}")

    observed = {operation.get("Operation"): operation for operation in operations}
    if set(observed) != set(EXPECTED_WINDOWS):
        fail(f"{name} operations mismatch: expected={sorted(EXPECTED_WINDOWS)} actual={sorted(observed)}")

    for operation, expected in EXPECTED_WINDOWS.items():
        window = observed[operation]
        for field in ("Expected", "Actual"):
            if window.get(field) != expected:
                fail(
                    f"{name} operation {operation!r} {field} was {window.get(field)!r}, expected {expected!r}\n"
                    f"{json.dumps(report, indent=2, sort_keys=True)}"
                )

    activities = report.get("Activities")
    expected_total = sum(len(spans) for spans in EXPECTED_WINDOWS.values())
    if not isinstance(activities, list) or len(activities) != expected_total:
        fail(f"{name} expected exactly {expected_total} MySqlConnector activities, got {activities!r}")


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
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
            "-c",
            "Release",
            "-r",
            runtime_identifier(),
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
        "Qyl.RealMySqlConnectorDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealMySqlConnectorDemo"
    )
    if not executable.exists():
        fail(f"NativeAOT MySqlConnector executable missing: {executable}")

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
    env["OTEL_SEMCONV_STABILITY_OPT_IN"] = STABILITY_OPT_IN
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="mysql",
        image=MYSQL_IMAGE,
        container_port=3306,
        container_env={
            "MYSQL_ROOT_PASSWORD": DATABASE,
            "MYSQL_DATABASE": DATABASE,
            "MYSQL_USER": DATABASE,
            "MYSQL_PASSWORD": DATABASE,
        },
        timeout_seconds=120,
    ) as mysql:
        env["QYL_MYSQL_CONNECTION_STRING"] = (
            f"Server={mysql.host};Port={mysql.port};User ID={DATABASE};"
            f"Password={DATABASE};Database={DATABASE}"
        )
        managed = run_managed(env)
        nativeaot = run_nativeaot(env)

    verify_report("managed MySqlConnector demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT MySqlConnector demo", nativeaot, "nativeaot")
    print("real-mysqlconnector-demo-ok")


if __name__ == "__main__":
    main()
