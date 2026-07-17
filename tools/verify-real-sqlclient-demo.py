#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import platform
import socket
import subprocess
import sys
from pathlib import Path
from typing import Any

from verify_container_helpers import run_published_container
from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealSqlClientDemo" / "Qyl.RealSqlClientDemo.csproj"
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


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str, port: int) -> None:
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
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 4:
        fail(f"{name} expected exactly 4 SqlClient activities, got {activities!r}")

    if f'"server.port": "{port}"' not in completed.stdout:
        fail(f"{name} did not capture the published SQL Server port {port}\nstdout={completed.stdout}")


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
    executable = output / ("Qyl.RealSqlClientDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealSqlClientDemo")
    if not executable.exists():
        fail(f"NativeAOT SqlClient executable missing: {executable}")

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
        managed = run_managed(env)
        nativeaot = run_nativeaot(env)

    verify_report("managed SqlClient demo", managed, "dynamic-code-supported", host_port)
    verify_report("NativeAOT SqlClient demo", nativeaot, "nativeaot", host_port)
    print("real-sqlclient-demo-ok")


if __name__ == "__main__":
    main()
