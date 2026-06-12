#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path
from typing import Any

from verify_container_helpers import run_published_container
from verify_helpers import clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealRedisDemo" / "Qyl.RealRedisDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
REDIS_IMAGE = os.environ.get("QYL_REDIS_IMAGE", "redis:8-alpine")


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"Redis demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"Redis demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"Redis demo report must be a JSON object: {report!r}")
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
        "stored=True",
        "value=alpha",
        "deleted=True",
        "expected-redis-error=RedisServerException",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 4:
        fail(f"{name} expected exactly 4 Redis activities, got {activities!r}")


def run_managed(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    assembly = PROJECT.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Qyl.RealRedisDemo.dll"
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
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
        name_prefix="redis",
        image=REDIS_IMAGE,
        container_port=6379,
    ) as redis:
        env["QYL_REDIS_CONFIGURATION"] = f"{redis.host}:{redis.port}"
        managed = run_managed(env)

    verify_report("managed Redis demo", managed, "dynamic-code-supported")
    print("real-redis-demo-ok")


if __name__ == "__main__":
    main()
