#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import platform
import subprocess
from pathlib import Path

from verify_container_helpers import run_published_container
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
PROJECT = ROOT / "demos" / "Qyl.RealRabbitMqDemo" / "Qyl.RealRabbitMqDemo.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
TARGET_FRAMEWORK = "net10.0"
RABBITMQ_IMAGE = os.environ.get("QYL_RABBITMQ_IMAGE", "rabbitmq:4.1-alpine")


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "published-queue=",
        "published-exchange=qyl-probe-exchange",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report("RabbitMQ demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 2:
        fail(f"{name} expected exactly 2 RabbitMQ activities, got {activities!r}")


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
            runtime_identifier("NativeAOT RabbitMQ gate"),
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
    executable = output / ("Qyl.RealRabbitMqDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealRabbitMqDemo")
    if not executable.exists():
        fail(f"NativeAOT RabbitMQ executable missing: {executable}")

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
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="rabbitmq",
        image=RABBITMQ_IMAGE,
        container_port=5672,
    ) as rabbitmq:
        env["QYL_RABBITMQ_URI"] = f"amqp://guest:guest@{rabbitmq.host}:{rabbitmq.port}/"
        managed = run_managed(PROJECT, env)
        nativeaot = run_nativeaot(env)

    verify_report("managed RabbitMQ demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT RabbitMQ demo", nativeaot, "nativeaot")
    print("real-rabbitmq-demo-ok")


if __name__ == "__main__":
    main()
