#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import subprocess
from pathlib import Path

from verify_container_helpers import run_published_container
from verify_helpers import clean_env, fail, find_free_port, parse_report, run_managed

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealKafkaDemo" / "Qyl.RealKafkaDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
KAFKA_IMAGE = os.environ.get("QYL_KAFKA_IMAGE", "apache/kafka:4.1.0")


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "produced-offset=",
        "consumed-messages=2",
        "expected-kafka-error=",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report("Kafka demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) < 5:
        fail(f"{name} expected at least 5 Kafka activities, got {activities!r}")


def main() -> None:
    env = clean_env()
    host_port = find_free_port()
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="kafka",
        image=KAFKA_IMAGE,
        container_port=9092,
        host_port=host_port,
        container_env={
            "KAFKA_NODE_ID": "1",
            "KAFKA_PROCESS_ROLES": "broker,controller",
            "KAFKA_LISTENERS": "PLAINTEXT://:9092,CONTROLLER://:9093",
            "KAFKA_ADVERTISED_LISTENERS": f"PLAINTEXT://127.0.0.1:{host_port}",
            "KAFKA_CONTROLLER_LISTENER_NAMES": "CONTROLLER",
            "KAFKA_LISTENER_SECURITY_PROTOCOL_MAP": "CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT",
            "KAFKA_CONTROLLER_QUORUM_VOTERS": "1@localhost:9093",
            "KAFKA_INTER_BROKER_LISTENER_NAME": "PLAINTEXT",
            "KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR": "1",
            "KAFKA_TRANSACTION_STATE_LOG_REPLICATION_FACTOR": "1",
            "KAFKA_TRANSACTION_STATE_LOG_MIN_ISR": "1",
            "KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS": "0",
        },
        timeout_seconds=120,
    ) as kafka:
        env["QYL_KAFKA_BOOTSTRAP_SERVERS"] = f"{kafka.host}:{kafka.port}"
        managed = run_managed(PROJECT, env)

    verify_report("managed Kafka demo", managed, "dynamic-code-supported")
    print("real-kafka-demo-ok")


if __name__ == "__main__":
    main()
