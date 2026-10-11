#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from verify_container_helpers import run_published_container
from verify_helpers import clean_env, fail, parse_report, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealMongoDbDemo" / "Qyl.RealMongoDbDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
MONGODB_IMAGE = os.environ.get("QYL_MONGODB_IMAGE", "mongo:8-noble")


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "inserted-documents=1",
        "found-documents=1",
        "deleted-documents=1",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report("MongoDB demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    # Three commands against the collection, each traced in two layers -- an operation span and the
    # command span beneath it. The driver's own handshake and topology commands name no collection
    # and are filtered out by the demo.
    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != 6:
        fail(f"{name} expected exactly 6 MongoDB collection activities, got {activities!r}")


def main() -> None:
    env = clean_env()
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="mongodb",
        image=MONGODB_IMAGE,
        container_port=27017,
        timeout_seconds=120,
    ) as mongodb:
        env["QYL_MONGODB_CONNECTION_STRING"] = f"mongodb://{mongodb.host}:{mongodb.port}/?serverSelectionTimeoutMS=2000"
        managed = run_managed(PROJECT, env)

    verify_report("managed MongoDB demo", managed, "dynamic-code-supported")
    print("real-mongodb-demo-ok")


if __name__ == "__main__":
    main()
