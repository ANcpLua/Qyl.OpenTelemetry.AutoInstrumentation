#!/usr/bin/env python3
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path

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
PROJECT = ROOT / "demos" / "Qyl.RealGrpcClientDemo" / "Qyl.RealGrpcClientDemo.csproj"
TARGET_FRAMEWORK = "net10.0"


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    if "expected-failure=Unavailable" not in completed.stdout:
        fail(f"{name} missing expected gRPC failure token\nstdout={completed.stdout}")

    report = parse_report("gRPC client demo", completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list):
        fail(f"{name} report Activities must be an array: {activities!r}")

    grpc_activities = [
        activity
        for activity in activities
        if isinstance(activity, dict)
        and isinstance(activity.get("Tags"), dict)
        and activity["Tags"].get("qyl.instrumentation.domain") == "rpc.grpc"
    ]
    if len(grpc_activities) != 2:
        fail(f"{name} expected exactly 2 gRPC client activities, got {grpc_activities!r}")


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
            runtime_identifier("NativeAOT gRPC client gate"),
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
    executable = output / ("Qyl.RealGrpcClientDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealGrpcClientDemo")
    if not executable.exists():
        fail(f"NativeAOT gRPC client executable missing: {executable}")

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
    nativeaot = run_nativeaot(env)
    verify_report("managed gRPC client demo", managed, "dynamic-code-supported")
    verify_report("NativeAOT gRPC client demo", nativeaot, "nativeaot")

    # The ClientBase<T> shape and raw CallInvoker shape share the same native
    # DiagnosticSource owner. Metadata is opt-in in both cases.
    client_env = dict(env)
    client_env["QYL_GRPC_DEMO_MODE"] = "client"
    verify_report("managed gRPC client demo (client lane)", run_managed(PROJECT, client_env), "dynamic-code-supported")
    verify_report("NativeAOT gRPC client demo (client lane)", run_nativeaot(client_env), "nativeaot")

    optin_env = dict(client_env)
    optin_env["OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA"] = "X-Demo-Md"
    optin_env["OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA"] = "X-Demo-Res-Md"
    verify_report("managed gRPC client demo (metadata opt-in)", run_managed(PROJECT, optin_env), "dynamic-code-supported")
    verify_report("NativeAOT gRPC client demo (metadata opt-in)", run_nativeaot(optin_env), "nativeaot")
    print("real-grpc-client-demo-ok")


if __name__ == "__main__":
    main()
