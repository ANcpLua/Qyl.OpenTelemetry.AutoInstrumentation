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
PROJECT = ROOT / "demos" / "Qyl.RealRedisDemo" / "Qyl.RealRedisDemo.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
TARGET_FRAMEWORK = "net10.0"
REDIS_IMAGE = os.environ.get("QYL_REDIS_IMAGE", "redis:8-alpine")

# Call sites whose command the generator resolves from an overload's parameter types or from an
# argument value, plus the ExecuteAsync sites that take the command name from the call itself.
REQUIRED_PROBES = {
    "StringGet": "GET",
    "StringGet.Multi": "MGET",
    "HashGet.Single": "HGET",
    "HashGet.Multi": "HMGET",
    "SetContains.Single": "SISMEMBER",
    "SetContains.Multi": "SMISMEMBER",
    "HashIncrement.Float": "HINCRBYFLOAT",
    "HashSet.Entries": "HMSET",
    # Reports SET although the wire receives SETNX: see s_wireEquivalents in the demo.
    "StringSet.NotExists": "SET",
    "StringSet.WhenNotExists": "SET",
    "HashSet.Field": "HSET",
    "HashSet.FieldNotExists": "HSETNX",
    "StringIncrement.Unit": "INCR",
    "StringIncrement.By": "INCRBY",
    "StringIncrement.Float": "INCRBYFLOAT",
    "StringDecrement.Unit": "DECR",
    "StringDecrement.By": "DECRBY",
    "ListLeftPush": "LPUSH",
    "ListLeftPush.Exists": "LPUSHX",
    "ListRightPush.Exists": "RPUSHX",
    "SortedSetRange.Ascending": "ZRANGE",
    "SortedSetRange.Descending": "ZREVRANGE",
    "KeyExpire.Seconds": "EXPIRE",
    "KeyExpire.Milliseconds": "PEXPIRE",
    "KeyExpire.SubMillisecondTicks": "EXPIRE",
    "KeyExpire.Persist": "PERSIST",
    "KeyExpire.At": "EXPIREAT",
    "KeyExpire.AtMilliseconds": "PEXPIREAT",
    "KeyExpire.AtPersist": "PERSIST",
    "Execute.Ping": "PING",
    "Execute.LowerCase": "PING",
    "Execute.Unknown": "QYLNOSUCH",
    "KeyTimeToLive": "PTTL",
}


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report("Redis demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    probes = report.get("Probes")
    if not isinstance(probes, list) or len(probes) < len(REQUIRED_PROBES):
        fail(f"{name} expected at least {len(REQUIRED_PROBES)} Redis probes, got {probes!r}")

    # The demo compares every span against the wire command itself. The gate additionally pins the
    # commands whose mapping is not a plain method-name lookup, so silently dropping one of those
    # call sites from the demo cannot quietly shrink the evidence.
    observed = {probe.get("Label"): probe.get("SpanOperation") for probe in probes}
    for label, expected_operation in REQUIRED_PROBES.items():
        if label not in observed:
            fail(f"{name} missing required probe {label!r}\n{json.dumps(report, indent=2, sort_keys=True)}")
        if observed[label] != expected_operation:
            fail(
                f"{name} probe {label!r} reported db.operation.name={observed[label]!r}, "
                f"expected {expected_operation!r}"
            )


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
            runtime_identifier("NativeAOT Redis gate"),
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
    executable = output / ("Qyl.RealRedisDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealRedisDemo")
    if not executable.exists():
        fail(f"NativeAOT Redis executable missing: {executable}")

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
        name_prefix="redis",
        image=REDIS_IMAGE,
        container_port=6379,
    ) as redis:
        env["QYL_REDIS_CONFIGURATION"] = f"{redis.host}:{redis.port}"
        managed = run_managed(PROJECT, env)
        nativeaot = run_nativeaot(env)

    verify_report("managed Redis demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT Redis demo", nativeaot, "nativeaot")
    print("real-redis-demo-ok")


if __name__ == "__main__":
    main()
