#!/usr/bin/env python3
"""Check what the instrumentation emits against the pinned semantic-convention registry.

`weaver registry live-check` listens on an OTLP/gRPC port and judges every span, metric and
resource it receives against the registry. This gate points the native-source demo lane at that
listener: one lane per `ActivitySource` qyl subscribes to instead of intercepting, so every span
the processor stamps `qyl.instrumentation.domain` on is judged, together with the qyl-owned spans
those demos emit alongside them.

A finding is never waved through here. `--fail-on violation` is weaver's own threshold, and a
violation is closed either by changing what the instrumentation writes or by declaring the key in
the registry. There is no allowlist in this file.
"""
from __future__ import annotations

import argparse
import os
import shutil
import socket
import subprocess
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path

from verify_helpers import LIVE_CHECK_ENDPOINT_VARIABLE, clean_env

ROOT = Path(__file__).resolve().parents[1]
REGISTRY_VARIABLE = "QYL_SEMCONV_REGISTRY"
OTLP_GRPC_PORT = 4317
ADMIN_PORT = 4320
INACTIVITY_TIMEOUT_SECONDS = 60
LISTENER_TIMEOUT_SECONDS = 60
SHUTDOWN_TIMEOUT_SECONDS = 120

# One lane per row of the native-source table. RabbitMQ carries two source names, publisher and
# subscriber, on the one lane; Elastic.Transport and Elasticsearch share one source and have a
# lane each, because the two clients put different attributes on it.
LANES = [
    "azure",
    "corewcf",
    "elastictransport",
    "elasticsearch",
    "masstransit",
    "mongodb",
    "nservicebus",
    "quartz",
    "rabbitmq",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def resolve_registry() -> Path:
    configured = os.environ.get(REGISTRY_VARIABLE)
    if not configured:
        fail(
            f"{REGISTRY_VARIABLE} must point at a checkout of the semantic-conventions repository "
            "whose registry this build pins"
        )

    registry_root = Path(configured).resolve()
    manifest = registry_root / "registry" / "manifest.yaml"
    if not manifest.is_file():
        fail(f"{REGISTRY_VARIABLE} has no registry/manifest.yaml: {registry_root}")

    # manifest.yaml names the filtered core copy by a path relative to the process working
    # directory, so weaver runs from the registry checkout and that copy has to exist first.
    core = registry_root / ".build" / "core-filtered" / "model"
    if not core.is_dir():
        fail(f"run scripts/fetch-core.sh in {registry_root} first: {core} is missing")

    return registry_root


def wait_for_listener(process: subprocess.Popen[str]) -> None:
    deadline = time.monotonic() + LISTENER_TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        if process.poll() is not None:
            fail(f"weaver exited before it opened port {OTLP_GRPC_PORT}: exit={process.returncode}")
        with socket.socket() as probe:
            probe.settimeout(1)
            if probe.connect_ex(("127.0.0.1", OTLP_GRPC_PORT)) == 0:
                return
        time.sleep(0.5)

    fail(f"weaver did not open port {OTLP_GRPC_PORT} within {LISTENER_TIMEOUT_SECONDS}s")


def stop_listener() -> None:
    request = urllib.request.Request(f"http://127.0.0.1:{ADMIN_PORT}/stop", method="POST")
    try:
        with urllib.request.urlopen(request, timeout=30):
            pass
    except urllib.error.URLError as error:
        fail(f"weaver admin port {ADMIN_PORT} did not accept /stop: {error}")


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "--skip",
        action="append",
        metavar="LANE[,LANE...]",
        default=[],
        help="Skip a demo lane whose container this machine cannot run. Named in the output.",
    )
    args = parser.parse_args()

    skipped = {lane.strip() for raw in args.skip for lane in raw.split(",") if lane.strip()}
    unknown = skipped - set(LANES)
    if unknown:
        fail(f"unknown lane(s): {sorted(unknown)}\navailable: {LANES}")

    if shutil.which("weaver") is None:
        fail("weaver is not on PATH")

    registry_root = resolve_registry()
    env = clean_env()
    env[LIVE_CHECK_ENDPOINT_VARIABLE] = f"http://127.0.0.1:{OTLP_GRPC_PORT}"

    command = [
        "weaver",
        "registry",
        "live-check",
        "--registry",
        str(registry_root / "registry"),
        # Core and genai are dependency registries; without this every key qyl does not itself
        # redeclare — service.name, http.request.method, error.type — reads as unknown.
        "--include-unreferenced",
        "--otlp-grpc-port",
        str(OTLP_GRPC_PORT),
        "--admin-port",
        str(ADMIN_PORT),
        "--inactivity-timeout",
        str(INACTIVITY_TIMEOUT_SECONDS),
        "--fail-on",
        "violation",
    ]
    print("== live check ==")
    print(" ".join(command))
    listener = subprocess.Popen(
        command,
        cwd=registry_root,
        env=dict(os.environ),
        text=True,
    )

    try:
        wait_for_listener(listener)
        for lane in LANES:
            if lane in skipped:
                print(f"== {lane} demo lane skipped ==")
                continue

            print(f"== {lane} demo lane ==")
            completed = subprocess.run(
                [sys.executable, f"tools/verify-real-{lane}-demo.py"],
                cwd=ROOT,
                env=env,
                check=False,
            )
            if completed.returncode != 0:
                fail(f"{lane} demo lane failed with exit code {completed.returncode}")
    finally:
        if listener.poll() is None:
            stop_listener()

    try:
        exit_code = listener.wait(timeout=SHUTDOWN_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        fail(f"weaver did not exit within {SHUTDOWN_TIMEOUT_SECONDS}s of /stop")

    if exit_code != 0:
        fail(f"live check reported findings at or above violation: exit={exit_code}")
    if skipped:
        print("live-check-partial-ok skipped=" + ",".join(sorted(skipped)))
    else:
        print("live-check-ok")


if __name__ == "__main__":
    main()
