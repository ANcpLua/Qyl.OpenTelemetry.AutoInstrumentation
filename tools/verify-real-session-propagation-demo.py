#!/usr/bin/env python3
"""Runs the session-propagation demo as TWO REAL PROCESSES and holds qyl to its own contract.

`session.id` is the one key qyl moves by itself. `QylSessionSpanProcessor.OnEnd` walks
`Activity.Parent`, so it sees in-process ancestors and nothing else, and `AddQyl` writes the value
onto a span and never into baggage. Three properties follow, and this gate is where they are
written down:

  1. an in-process descendant of a span carrying `session.id` INHERITS the value,
  2. a descendant that already carries its own `session.id` is NOT overwritten,
  3. across a real HTTP boundary into a SECOND PROCESS the server span carries NO `session.id`,
     although `traceparent` arrived, the trace is the same one, and the upstream's session even
     reached that span in baggage.

Two hosts inside one process would leave `Activity.Parent` linked across the "boundary" and (3)
would pass while being false. So this verifier — and only this verifier, never the demo binary —
starts the two processes, one per role, and reads each one's report separately: each process runs
its own SDK pipeline and its own in-memory exporter, and its report has to name the operating-system
process id this script actually launched.

The downstream role also proves its own propagation works in the same run, on a locally rooted pair
of spans. Without that control, a downstream whose processor was missing entirely would satisfy (3)
by doing nothing at all.
"""
from __future__ import annotations

import json
import socket
import subprocess
import time
from pathlib import Path
from typing import Any, NoReturn

from verify_helpers import artifacts_bin_assembly, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealSessionPropagationDemo" / "Qyl.RealSessionPropagationDemo.csproj"

DEMO_SOURCE = "Qyl.RealSessionPropagationDemo"
ASPNETCORE_SOURCE = "Microsoft.AspNetCore"
HTTPCLIENT_SOURCE = "System.Net.Http"
SESSION_TAG = "session.id"

# The demo asserts these itself; the verifier pins the same values so a demo that quietly relaxes
# its own expectation still fails here.
SESSION_ID = "qyl-real-session-propagation-demo-session"
OWN_SESSION_ID = "qyl-real-session-propagation-demo-own-session"
DOWNSTREAM_SESSION_ID = "qyl-real-session-propagation-demo-downstream-session"

ROOT_SPAN = (DEMO_SOURCE, "upstream root")
INHERITED_SPAN = (DEMO_SOURCE, "upstream inherits session")
OWN_SESSION_SPAN = (DEMO_SOURCE, "upstream owns session")
CLIENT_SPAN = (HTTPCLIENT_SOURCE, "GET")
SERVER_SPAN = (ASPNETCORE_SOURCE, "GET /probe")
PROBE_CHILD_SPAN = (DEMO_SOURCE, "downstream probe child")
LOCAL_ROOT_SPAN = (DEMO_SOURCE, "downstream local root")
LOCAL_CHILD_SPAN = (DEMO_SOURCE, "downstream local child")

EMPTY_SPAN_ID = "0000000000000000"

# Every wait has an upper bound, and every started process is terminated in a finally. A demo that
# hangs fails the gate on time rather than holding a runner.
PORT_WAIT_SECONDS = 30.0
PORT_POLL_SECONDS = 0.05
UPSTREAM_TIMEOUT_SECONDS = 120.0
DOWNSTREAM_EXIT_TIMEOUT_SECONDS = 60.0
TERMINATE_TIMEOUT_SECONDS = 10.0


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def reserve_port() -> int:
    """One free loopback port, handed to the downstream role on its command line."""
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def wait_for_port(port: int, process: subprocess.Popen[str]) -> None:
    """Bounded wait for the downstream listener. No endless loop, and a dead process ends it early."""
    deadline = time.monotonic() + PORT_WAIT_SECONDS
    while time.monotonic() < deadline:
        if process.poll() is not None:
            stdout, stderr = process.communicate()
            fail(f"downstream exited before it listened\nexit={process.returncode}\nstdout={stdout}\nstderr={stderr}")

        with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
            probe.settimeout(PORT_POLL_SECONDS)
            if probe.connect_ex(("127.0.0.1", port)) == 0:
                return

        time.sleep(PORT_POLL_SECONDS)

    fail(f"downstream did not listen on 127.0.0.1:{port} within {PORT_WAIT_SECONDS:.0f}s")


def terminate(process: subprocess.Popen[str] | None) -> None:
    if process is None or process.poll() is not None:
        return

    process.kill()
    try:
        process.communicate(timeout=TERMINATE_TIMEOUT_SECONDS)
    except subprocess.TimeoutExpired:
        pass


def start(assembly: Path, role: str, port: int, env: dict[str, str]) -> subprocess.Popen[str]:
    return subprocess.Popen(
        ["dotnet", str(assembly), "--role", role, "--port", str(port)],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
    )


def parse_report(name: str, stdout: str) -> dict[str, Any]:
    start_index = stdout.find("{\n")
    if start_index < 0:
        fail(f"{name} did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start_index:])
    except json.JSONDecodeError as exc:
        fail(f"{name} emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"{name} report must be a JSON object: {report!r}")
    return report


def verify_process(
    name: str,
    role: str,
    process_id: int,
    returncode: int,
    stdout: str,
    stderr: str,
) -> dict[str, Any]:
    if returncode != 0:
        fail(f"{name} failed\nexit={returncode}\nstdout={stdout}\nstderr={stderr}")
    if stderr:
        fail(f"{name} wrote stderr:\n{stderr}")

    report = parse_report(name, stdout)
    if report.get("Role") != role:
        fail(f"{name} reported role {report.get('Role')!r}, expected {role!r}")
    if report.get("RuntimeMode") != "dynamic-code-supported":
        fail(f"{name} runtime mode mismatch: {report.get('RuntimeMode')!r}")
    if report.get("SessionPropagationEnabled") is not True:
        fail(f"{name} ran with session propagation off — the gate would prove nothing:\n{report!r}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")
    # The report has to belong to the operating-system process this script started. A shared
    # in-memory exporter, or one process reporting for both roles, dies here.
    if report.get("ProcessId") != process_id:
        fail(f"{name} reported process id {report.get('ProcessId')!r}, but was launched as {process_id}")

    return report


def span(name: str, report: dict[str, Any], key: tuple[str, str]) -> dict[str, Any]:
    source, span_name = key
    matches = [
        activity for activity in report.get("Activities") or []
        if isinstance(activity, dict)
        and activity.get("Source") == source
        and activity.get("Name") == span_name
    ]
    if len(matches) != 1:
        fail(f"{name} exported {len(matches)} spans named {source}/{span_name}, expected exactly 1")

    return matches[0]


def require_tag(name: str, activity: dict[str, Any], key: str, expected: str) -> None:
    tags = activity.get("Tags")
    if not isinstance(tags, dict):
        fail(f"{name} span {activity.get('Name')!r} carries no tags: {activity!r}")
    if tags.get(key) != expected:
        fail(f"{name} span {activity.get('Name')!r} expected {key}={expected!r}, got {tags.get(key)!r}")


def require_no_tag(name: str, activity: dict[str, Any], key: str) -> None:
    tags = activity.get("Tags")
    if not isinstance(tags, dict):
        fail(f"{name} span {activity.get('Name')!r} carries no tags: {activity!r}")
    if key in tags:
        fail(
            f"{name} span {activity.get('Name')!r} carries {key}={tags[key]!r} across a process "
            "boundary — qyl copies the session onto a remote-parented span"
        )


def require_baggage(name: str, activity: dict[str, Any], key: str, expected: str) -> None:
    baggage = activity.get("Baggage")
    if not isinstance(baggage, dict):
        fail(f"{name} span {activity.get('Name')!r} carries no baggage map: {activity!r}")
    if baggage.get(key) != expected:
        fail(f"{name} span {activity.get('Name')!r} expected baggage {key}={expected!r}, got {baggage.get(key)!r}")


def verify_contract(upstream: dict[str, Any], downstream: dict[str, Any]) -> None:
    # Two processes, not two hosts. The pids were already matched against the launched processes;
    # this is the statement that they are two different ones.
    if upstream["ProcessId"] == downstream["ProcessId"]:
        fail(f"both roles reported process id {upstream['ProcessId']} — they ran in one process")

    root = span("upstream", upstream, ROOT_SPAN)
    inherited = span("upstream", upstream, INHERITED_SPAN)
    own = span("upstream", upstream, OWN_SESSION_SPAN)
    client = span("upstream", upstream, CLIENT_SPAN)
    server = span("downstream", downstream, SERVER_SPAN)
    probe_child = span("downstream", downstream, PROBE_CHILD_SPAN)
    local_root = span("downstream", downstream, LOCAL_ROOT_SPAN)
    local_child = span("downstream", downstream, LOCAL_CHILD_SPAN)

    require_tag("upstream", root, SESSION_TAG, SESSION_ID)

    # PROPERTY 1 — an in-process descendant inherits the ancestor's session, by value.
    require_tag("upstream", inherited, SESSION_TAG, SESSION_ID)
    if inherited.get("HasInProcessParent") is not True:
        fail("upstream 'inherits session' span has no in-process parent — it inherited from nothing")

    # PROPERTY 2 — a descendant that brought its own session keeps it; the ancestor does not win.
    require_tag("upstream", own, SESSION_TAG, OWN_SESSION_ID)

    # The BCL's client span is an in-process descendant like any other.
    require_tag("upstream", client, SESSION_TAG, SESSION_ID)
    if client.get("TraceId") != root.get("TraceId"):
        fail(f"upstream client span is on trace {client.get('TraceId')!r}, root is on {root.get('TraceId')!r}")

    # PROPERTY 3 — one trace, a real remote parent, and no session on the server span.
    if server.get("TraceId") != client.get("TraceId"):
        fail(
            f"downstream server span is on trace {server.get('TraceId')!r} but the upstream client span "
            f"is on {client.get('TraceId')!r} — the two processes are not on one trace"
        )
    if server.get("ParentSpanId") != client.get("SpanId"):
        fail(
            f"downstream server span parents to {server.get('ParentSpanId')!r}, the upstream client span "
            f"is {client.get('SpanId')!r} — traceparent did not cross the boundary"
        )
    if server.get("ParentSpanId") == EMPTY_SPAN_ID:
        fail("downstream server span has no remote parent at all")
    if server.get("HasInProcessParent") is not False:
        fail("downstream server span has an IN-PROCESS parent — the roles did not run in two processes")
    require_no_tag("downstream", server, SESSION_TAG)
    # The session did reach the second process — the application put it in baggage and the BCL
    # carried it. qyl still wrote no tag. That is the whole answer.
    require_baggage("downstream", server, SESSION_TAG, SESSION_ID)
    require_no_tag("downstream", probe_child, SESSION_TAG)

    # The control: propagation is installed and working in the downstream process, so the three
    # assertions above measure the boundary and not a missing processor.
    require_tag("downstream", local_root, SESSION_TAG, DOWNSTREAM_SESSION_ID)
    require_tag("downstream", local_child, SESSION_TAG, DOWNSTREAM_SESSION_ID)
    if local_child.get("HasInProcessParent") is not True:
        fail("downstream control child has no in-process parent — the control proves nothing")
    if local_child.get("TraceId") == server.get("TraceId"):
        fail("downstream control span shares the request trace — it is not the locally rooted control")


def main() -> None:
    env = clean_env()
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    assembly = artifacts_bin_assembly(PROJECT)
    port = reserve_port()

    downstream_process: subprocess.Popen[str] | None = None
    upstream_process: subprocess.Popen[str] | None = None
    try:
        # Exactly two processes per run, both started here and nowhere else.
        downstream_process = start(assembly, "downstream", port, env)
        wait_for_port(port, downstream_process)

        upstream_process = start(assembly, "upstream", port, env)
        try:
            upstream_stdout, upstream_stderr = upstream_process.communicate(timeout=UPSTREAM_TIMEOUT_SECONDS)
        except subprocess.TimeoutExpired:
            fail(f"upstream did not finish within {UPSTREAM_TIMEOUT_SECONDS:.0f}s")

        # The downstream stops itself once the one probe has been answered.
        try:
            downstream_stdout, downstream_stderr = downstream_process.communicate(
                timeout=DOWNSTREAM_EXIT_TIMEOUT_SECONDS
            )
        except subprocess.TimeoutExpired:
            fail(
                f"downstream did not exit within {DOWNSTREAM_EXIT_TIMEOUT_SECONDS:.0f}s of the probe\n"
                f"upstream stdout={upstream_stdout}\nupstream stderr={upstream_stderr}"
            )

        upstream_returncode = upstream_process.returncode
        downstream_returncode = downstream_process.returncode
        upstream_pid = upstream_process.pid
        downstream_pid = downstream_process.pid
    finally:
        terminate(upstream_process)
        terminate(downstream_process)

    if "probe-status=204" not in upstream_stdout:
        fail(f"upstream did not reach the downstream probe\nstdout={upstream_stdout}")

    upstream = verify_process(
        "upstream demo", "upstream", upstream_pid, upstream_returncode, upstream_stdout, upstream_stderr
    )
    downstream = verify_process(
        "downstream demo", "downstream", downstream_pid, downstream_returncode, downstream_stdout, downstream_stderr
    )
    verify_contract(upstream, downstream)
    print("real-session-propagation-demo-ok")


if __name__ == "__main__":
    main()
