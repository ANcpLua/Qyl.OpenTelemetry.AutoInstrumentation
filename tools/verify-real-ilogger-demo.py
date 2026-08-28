#!/usr/bin/env python3
"""Real ILogger LogRecord gate.

AddQyl() registers the OpenTelemetry ILogger provider, so an ILogger call becomes a LogRecord
and never a qyl Activity. Two scenarios per runtime: the global logs control unset (records
exported) and set to false (provider unregistered, nothing exported).
"""
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealILoggerDemo" / "Qyl.RealILoggerDemo.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
TARGET_FRAMEWORK = "net10.0"
LOGS_CONTROL_VARIABLE = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED"
EXPECTED_RECORDS = [
    ("Trace", "qyl trace record"),
    ("Debug", "qyl debug record"),
    ("Information", "qyl information record"),
    ("Warning", "qyl warning record"),
    ("Error", "qyl error record"),
    ("Critical", "qyl critical record"),
]


def fail(message: str) -> None:
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

    fail(f"unsupported NativeAOT ILogger gate platform: {platform.system()} {platform.machine()}")


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"ILogger demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"ILogger demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"ILogger demo report must be a JSON object: {report!r}")
    return report


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    expected_runtime_mode: str,
    *,
    logs_enabled: bool,
) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    expected_count = len(EXPECTED_RECORDS) if logs_enabled else 0
    if f"ilogger-record-count={expected_count}" not in completed.stdout:
        fail(f"{name} did not export {expected_count} log records\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")
    if report.get("LogsControlEnabled") is not logs_enabled:
        fail(f"{name} logs control mismatch: expected={logs_enabled} actual={report.get('LogsControlEnabled')}")
    if report.get("OpenTelemetryLoggerProviderRegistered") is not logs_enabled:
        fail(f"{name} logging provider registration mismatch:\n{json.dumps(report, indent=2, sort_keys=True)}")

    # The regression assertion for the deleted log-as-span lane.
    activities = report.get("QylActivities")
    if activities != []:
        fail(f"{name} produced qyl activities for log calls: {activities!r}")

    records = report.get("Records")
    if not isinstance(records, list) or len(records) != expected_count:
        fail(f"{name} expected {expected_count} exported log records, got {records!r}")
    if not logs_enabled:
        return

    actual = [(record.get("Severity"), record.get("Body")) for record in records]
    if actual != EXPECTED_RECORDS:
        fail(f"{name} log record severity/body mismatch: {actual!r}")


def run_scenario(command: list[str], cwd: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        command,
        cwd=cwd,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def run_managed(env: dict[str, str]) -> Path:
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    return artifacts_bin_assembly(PROJECT)


def run_nativeaot(env: dict[str, str]) -> Path:
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
    executable = output / ("Qyl.RealILoggerDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealILoggerDemo")
    if not executable.exists():
        fail(f"NativeAOT ILogger executable missing: {executable}")

    return executable


def main() -> None:
    env = clean_env()
    logs_disabled_env = dict(env)
    logs_disabled_env[LOGS_CONTROL_VARIABLE] = "false"

    assembly = run_managed(env)
    executable = run_nativeaot(env)

    verify_report(
        "managed ILogger demo",
        run_scenario(["dotnet", str(assembly)], PROJECT.parent, env),
        "dynamic-code-supported",
        logs_enabled=True,
    )
    verify_report(
        f"managed ILogger demo ({LOGS_CONTROL_VARIABLE}=false)",
        run_scenario(["dotnet", str(assembly)], PROJECT.parent, logs_disabled_env),
        "dynamic-code-supported",
        logs_enabled=False,
    )
    verify_report(
        "NativeAOT ILogger demo",
        run_scenario([str(executable)], executable.parent, env),
        "nativeaot",
        logs_enabled=True,
    )
    verify_report(
        f"NativeAOT ILogger demo ({LOGS_CONTROL_VARIABLE}=false)",
        run_scenario([str(executable)], executable.parent, logs_disabled_env),
        "nativeaot",
        logs_enabled=False,
    )
    print("real-ilogger-demo-ok")


if __name__ == "__main__":
    main()
