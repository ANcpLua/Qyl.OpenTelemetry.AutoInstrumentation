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

How a finding is *levelled* is the registry's decision, not this repository's. The registry ships
`registry/policies/live_check_advice/`, and this gate passes it to `--advice-policies`: an open
enum with `_OTHER` is information, a renamed or obsoleted key a library still emits is an
improvement, a type mismatch whose value parses is an improvement. Without that policy set weaver
falls back to its default advisor, which calls all three violations, so a missing policy set fails
the gate here rather than quietly changing what the threshold means.
"""
from __future__ import annotations

import argparse
import os
import re
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
PACKAGES_PROPS = ROOT / "Directory.Packages.props"
SEMCONV_PACKAGE = "Qyl.Telemetry.SemanticConventions"
REGISTRY_VARIABLE = "QYL_SEMCONV_REGISTRY"
ADVICE_POLICIES = Path("registry") / "policies" / "live_check_advice"
WEAVER_CONFIG = Path("registry") / ".weaver.toml"
OTLP_GRPC_PORT = 4317
ADMIN_PORT = 4320
LISTENER_TIMEOUT_SECONDS = 60
SHUTDOWN_TIMEOUT_SECONDS = 120

# Bounds one demo lane end to end: the container pull, the managed build, the NativeAOT publish
# and the two runs. A lane that exceeds it is hung, and the gate says so instead of waiting.
LANE_TIMEOUT_SECONDS = 1200

# weaver's inactivity timer resets on every span it receives, so the longest silence it has to
# survive is one whole cold lane — nothing reaches the listener while MassTransit pulls RabbitMQ
# and NativeAOT-publishes. Derived from the lane bound, and larger than it, so LANE_TIMEOUT_SECONDS
# is always the enforcer and weaver never self-terminates mid-run. At 60s it did: locally the
# listener stopped after 184s, the lanes that followed exported into a closed port, and the gate
# still reported success because nothing checked whether the listener was alive.
INACTIVITY_TIMEOUT_SECONDS = LANE_TIMEOUT_SECONDS + 120

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


def read_property(path: Path, pattern: str, what: str) -> str:
    match = re.search(pattern, path.read_text(encoding="utf-8"))
    if not match:
        fail(f"{path} declares no {what}")

    return match.group(1)


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

    # The registry checkout and the package pin are named in different files — the workflow's ref
    # and Directory.Packages.props — so nothing but this check keeps them the same release. Judging
    # spans against a registry other than the one whose constants the code compiled against is the
    # failure that would look like a green gate.
    pinned = read_property(
        PACKAGES_PROPS,
        rf'<PackageVersion\s+Include="{re.escape(SEMCONV_PACKAGE)}"\s+Version="([^"]+)"',
        f"{SEMCONV_PACKAGE} PackageVersion",
    )
    checked_out = read_property(
        registry_root / "Directory.Build.props",
        r"<VersionPrefix[^>]*>([^<]+)</VersionPrefix>",
        "VersionPrefix",
    )
    if pinned != checked_out:
        fail(
            f"{REGISTRY_VARIABLE} is the {checked_out} registry, but this repository pins "
            f"{SEMCONV_PACKAGE} {pinned}. Check out v{pinned}."
        )

    # manifest.yaml names the filtered core copy by a path relative to the process working
    # directory, so weaver runs from the registry checkout and that copy has to exist first.
    core = registry_root / ".build" / "core-filtered" / "model"
    if not core.is_dir():
        fail(f"run scripts/fetch-core.sh in {registry_root} first: {core} is missing")

    # The policy set has two halves and both are checked here, because weaver reports neither an
    # unreadable --advice-policies directory nor a missing --config: it would judge the run with
    # its built-in advisors, call every renamed key a library emits a violation, and this file
    # rather than the registry would own what a violation means.
    #
    # .weaver.toml drops the built-in deprecated/type/enum findings by finding id — those advisors
    # are compiled into the binary and emit at a level no policy can lower — and the rego policies
    # re-issue them at the level the registry chose.
    policies = registry_root / ADVICE_POLICIES
    rego = sorted(policies.glob("*.rego")) if policies.is_dir() else []
    if not rego:
        fail(f"the {checked_out} registry ships no advice policies at {ADVICE_POLICIES}")

    if not (registry_root / WEAVER_CONFIG).is_file():
        fail(f"the {checked_out} registry ships no {WEAVER_CONFIG}, so the built-in advisors stand")

    print(f"registry {checked_out}: {registry_root}")
    print("advice policies: " + ", ".join(path.name for path in rego))
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


def ensure_listener_alive(listener: subprocess.Popen[str], where: str) -> None:
    """Stop the gate the moment weaver is gone, with weaver's own exit code.

    A listener that has exited leaves port 4317 closed, and a demo that exports into a closed
    port still passes its own in-memory assertions — so without this check the lanes after the
    exit are judged by nobody while the gate still ends green. An inactivity timeout exits 0,
    which would be the worst of the two, so a zero exit code is reported as failure here.
    """
    exit_code = listener.poll()
    if exit_code is None:
        return

    print(
        f"weaver stopped listening {where}: exit={exit_code}. "
        f"Every span after that point was exported into a closed port and judged by nobody.",
        file=sys.stderr,
    )
    raise SystemExit(exit_code or 1)


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
        # The registry decides what level a finding gets; weaver's built-in advisors would decide
        # it here. Open enums with _OTHER, the deprecated keys the libraries themselves emit and a
        # parseable type mismatch are not violations, and the registry is where that is written.
        # Both halves are required: --config drops the built-in findings, --advice-policies
        # re-issues them at the registry's level.
        "--config",
        str(registry_root / WEAVER_CONFIG),
        "--advice-policies",
        str(registry_root / ADVICE_POLICIES),
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
            ensure_listener_alive(listener, f"before the {lane} demo lane")
            try:
                completed = subprocess.run(
                    [sys.executable, f"tools/verify-real-{lane}-demo.py"],
                    cwd=ROOT,
                    env=env,
                    check=False,
                    timeout=LANE_TIMEOUT_SECONDS,
                )
            except subprocess.TimeoutExpired:
                fail(f"{lane} demo lane did not finish within {LANE_TIMEOUT_SECONDS}s")

            if completed.returncode != 0:
                fail(f"{lane} demo lane failed with exit code {completed.returncode}")
            ensure_listener_alive(listener, f"during the {lane} demo lane")
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
