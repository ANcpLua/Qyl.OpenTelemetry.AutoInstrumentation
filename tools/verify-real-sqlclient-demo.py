#!/usr/bin/env python3
from __future__ import annotations

import json
import importlib.util
import os
import platform
import socket
import subprocess
import sys
from pathlib import Path
from types import ModuleType
from typing import Any

from verify_container_helpers import run_published_container
from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealSqlClientDemo" / "Qyl.RealSqlClientDemo.csproj"
AOT_PUBLISH_GATE = ROOT / "tools" / "verify-aot-publish-gate.py"
TARGET_FRAMEWORK = "net10.0"
SQLSERVER_IMAGE = os.environ.get("QYL_SQLSERVER_IMAGE", "mcr.microsoft.com/mssql/server:2022-latest")
SQL_PASSWORD = os.environ.get("QYL_SQL_PASSWORD", "Qyl_strong_Password_2026!")


def fail(message: str) -> None:
    raise SystemExit(message)


def find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"SqlClient demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"SqlClient demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"SqlClient demo report must be a JSON object: {report!r}")
    return report


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    expected_runtime_mode: str,
    expected_trace_owner: str,
    expected_metric_count: int,
    port: int,
) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if "expected-sql-error=208" not in completed.stdout:
        fail(f"{name} missing expected SqlClient error token\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("TraceOwner") != expected_trace_owner:
        fail(f"{name} trace owner mismatch: expected={expected_trace_owner} actual={report.get('TraceOwner')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 4:
        fail(f"{name} expected exactly 4 SqlClient activities, got {activities!r}")
    metrics = report.get("Metrics")
    if not isinstance(metrics, list) or len(metrics) != expected_metric_count:
        fail(f"{name} expected exactly {expected_metric_count} SqlClient duration metrics, got {metrics!r}")

    if expected_trace_owner == "specialist_listener" and f'"server.port": "{port}"' not in completed.stdout:
        fail(f"{name} did not capture the published SQL Server port {port}\nstdout={completed.stdout}")


def source_interceptor_property(enabled: bool) -> str:
    return f"-p:QylSqlClientSourceInterceptors={'true' if enabled else 'false'}"


def load_aot_publish_gate() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_aot_publish_gate_for_sqlclient", AOT_PUBLISH_GATE)
    if spec is None or spec.loader is None:
        fail(f"cannot load NativeAOT publish gate: {AOT_PUBLISH_GATE}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build_managed(env: dict[str, str], *, source_interceptors: bool) -> Path:
    run_checked(
        [
            "dotnet",
            "build",
            str(PROJECT),
            "-c",
            "Release",
            "-v",
            "quiet",
            "--no-incremental",
            "--disable-build-servers",
            "-m:1",
            source_interceptor_property(source_interceptors),
        ],
        ROOT,
        env,
    )
    return artifacts_bin_assembly(PROJECT)


def run_managed(assembly: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def publish_nativeaot(env: dict[str, str], *, source_interceptors: bool) -> Path:
    name = "nativeaot" if source_interceptors else "nativeaot-specialist"
    output = artifacts_publish_dir(PROJECT, name)
    if source_interceptors:
        if env.get("AOT_PUBLISH_GATE_SET") not in {"warned", "all"}:
            run_checked(
                [sys.executable, "tools/verify-aot-publish-gate.py", "--set", "warned", "--demo",
                 PROJECT.stem, "--strict-promotion", "--keep-publish"], ROOT, env)
    else:
        gate = load_aot_publish_gate()
        rid = runtime_identifier()
        code, diagnostics, tail = gate.publish(
            PROJECT,
            output,
            strict=False,
            rid=rid,
            env=env,
            extra_props=[source_interceptor_property(False)],
        )
        if code != 0:
            detail = diagnostics[0] if diagnostics else tail
            fail(f"specialist-only NativeAOT SqlClient publish failed: exit={code}; {detail}")
        if not diagnostics:
            fail("specialist-only NativeAOT SqlClient publish is now warning-clean; promote its vendor boundary")
        valid, detail = gate.validate_warning_policy(PROJECT.stem, PROJECT, diagnostics, rid)
        if not valid:
            fail(f"specialist-only NativeAOT SqlClient warning policy failed: {detail}")
    executable = output / ("Qyl.RealSqlClientDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealSqlClientDemo")
    if not executable.exists():
        fail(f"NativeAOT SqlClient executable missing: {executable}")
    return executable


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    arm = machine in {"arm64", "aarch64"}
    if system == "darwin":
        return "osx-arm64" if arm else "osx-x64"
    if system == "linux":
        return "linux-arm64" if arm else "linux-x64"
    if system == "windows":
        return "win-arm64" if arm else "win-x64"
    fail(f"unsupported NativeAOT SqlClient platform: {system}/{machine}")


def run_nativeaot(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    host_port = find_free_port()
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="sqlclient",
        image=SQLSERVER_IMAGE,
        container_port=1433,
        host_port=host_port,
        platform="linux/amd64",
        container_env={
            "ACCEPT_EULA": "Y",
            "MSSQL_SA_PASSWORD": SQL_PASSWORD,
        },
        timeout_seconds=120,
    ) as sqlserver:
        env["QYL_SQLCLIENT_CONNECTION_STRING"] = (
            f"Server={sqlserver.host},{sqlserver.port};"
            f"User ID=sa;Password={SQL_PASSWORD};Initial Catalog=tempdb;"
            "Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"
        )
        env["QYL_SQLCLIENT_EXPECTED_PORT"] = str(sqlserver.port)
        source_env = dict(env)
        source_env["QYL_SQLCLIENT_EXPECTED_TRACE_OWNER"] = "source_interceptor"
        source_optin_env = dict(source_env)
        source_optin_env["OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT"] = "true"
        source_managed_assembly = build_managed(source_env, source_interceptors=True)
        source_managed = run_managed(source_managed_assembly, source_env)
        source_managed_optin = run_managed(source_managed_assembly, source_optin_env)
        source_native_executable = publish_nativeaot(source_env, source_interceptors=True)
        source_native = run_nativeaot(source_native_executable, source_env)
        source_native_optin = run_nativeaot(source_native_executable, source_optin_env)

        specialist_env = dict(env)
        specialist_env["QYL_SQLCLIENT_EXPECTED_TRACE_OWNER"] = "specialist_listener"
        specialist_optin_env = dict(specialist_env)
        specialist_optin_env["OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT"] = "true"
        specialist_managed_assembly = build_managed(specialist_env, source_interceptors=False)
        specialist_managed = run_managed(specialist_managed_assembly, specialist_env)
        specialist_managed_optin = run_managed(specialist_managed_assembly, specialist_optin_env)
        specialist_native_executable = publish_nativeaot(specialist_env, source_interceptors=False)
        specialist_native = run_nativeaot(specialist_native_executable, specialist_env)
        specialist_native_optin = run_nativeaot(specialist_native_executable, specialist_optin_env)

    source_runs = [
        ("managed SqlClient source-interceptor demo", source_managed, "dynamic-code-supported"),
        ("managed SqlClient source-interceptor demo (statement opt-in)", source_managed_optin, "dynamic-code-supported"),
        ("NativeAOT SqlClient source-interceptor demo", source_native, "nativeaot"),
        ("NativeAOT SqlClient source-interceptor demo (statement opt-in)", source_native_optin, "nativeaot"),
    ]
    for name, completed, runtime_mode in source_runs:
        verify_report(name, completed, runtime_mode, "source_interceptor", 4, host_port)

    specialist_runs = [
        ("managed SqlClient specialist demo", specialist_managed, "dynamic-code-supported"),
        ("managed SqlClient specialist demo (statement opt-in)", specialist_managed_optin, "dynamic-code-supported"),
        ("NativeAOT SqlClient specialist demo", specialist_native, "nativeaot"),
        ("NativeAOT SqlClient specialist demo (statement opt-in)", specialist_native_optin, "nativeaot"),
    ]
    for name, completed, runtime_mode in specialist_runs:
        verify_report(name, completed, runtime_mode, "specialist_listener", 0, host_port)
    print("real-sqlclient-demo-ok")


if __name__ == "__main__":
    main()
