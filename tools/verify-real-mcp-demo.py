#!/usr/bin/env python3
from __future__ import annotations

from collections import Counter
import json
import platform
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealMcpDemo" / "Qyl.RealMcpDemo.csproj"
MCP_SOURCE = "Experimental.ModelContextProtocol"
SENSITIVE_VALUES = (
    "mcp-sensitive-argument-73f8a9",
    "mcp-sensitive-result-9d20c4",
)
EXPECTED_OPERATIONS = {
    "initialize": "initialize",
    "notifications/initialized": "notifications/initialized",
    "tools/list": "tools/list",
    "tools/call qyl_sensitive_probe": "tools/call",
}


def fail(message: str) -> None:
    raise SystemExit(message)


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    arm = machine in {"arm64", "aarch64"}
    if system == "darwin":
        return "osx-arm64" if arm else "osx-x64"
    if system == "linux":
        return "linux-arm64" if arm else "linux-x64"
    if system == "windows":
        return "win-arm64" if arm else "win-x64"
    fail(f"unsupported NativeAOT MCP gate platform: {system}/{machine}")


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"MCP demo did not emit a JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"MCP demo emitted invalid JSON: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"MCP report must be an object: {report!r}")
    return report


def require_tags(activity: dict[str, Any], expected: dict[str, str]) -> None:
    tags = activity.get("Tags")
    if not isinstance(tags, dict):
        fail(f"MCP activity has invalid tags: {activity!r}")
    for key, value in expected.items():
        if tags.get(key) != value:
            fail(f"MCP activity tag mismatch for {key}: expected={value!r} activity={activity!r}")


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    expected_runtime_mode: str,
    *,
    registration_enabled: bool,
) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")
    for sensitive in SENSITIVE_VALUES:
        if sensitive in completed.stdout:
            fail(f"{name} leaked a sensitive tool value to telemetry output: {sensitive}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(
            f"{name} runtime mode mismatch: "
            f"expected={expected_runtime_mode!r} actual={report.get('RuntimeMode')!r}"
        )
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")
    if report.get("ToolListed") is not True or report.get("ToolCalled") is not True:
        fail(f"{name} tool discovery/invocation failed: {report!r}")
    if report.get("QylRegistrationEnabled") is not registration_enabled:
        fail(f"{name} registration state mismatch: {report!r}")

    activities = report.get("Activities")
    expected_count = 8 if registration_enabled else 0
    if not isinstance(activities, list) or len(activities) != expected_count:
        fail(f"{name} expected exactly {expected_count} activities, got {activities!r}")
    if not registration_enabled:
        return
    if not all(isinstance(activity, dict) for activity in activities):
        fail(f"{name} activities must be objects: {activities!r}")

    activity_objects: list[dict[str, Any]] = activities
    if {activity.get("Source") for activity in activity_objects} != {MCP_SOURCE}:
        fail(f"{name} ActivitySource mismatch: {activity_objects!r}")
    if Counter(activity.get("Name") for activity in activity_objects) != Counter(
        {name: 2 for name in EXPECTED_OPERATIONS}
    ):
        fail(f"{name} operation set mismatch: {activity_objects!r}")

    root_trace_id = report.get("RootTraceId")
    root_span_id = report.get("RootSpanId")
    if not isinstance(root_trace_id, str) or not isinstance(root_span_id, str):
        fail(f"{name} report is missing root context: {report!r}")

    sessions: dict[str, set[str]] = {"Client": set(), "Server": set()}
    for activity_name, method in EXPECTED_OPERATIONS.items():
        pair = [activity for activity in activity_objects if activity.get("Name") == activity_name]
        by_kind = {activity.get("Kind"): activity for activity in pair}
        if set(by_kind) != {"Client", "Server"}:
            fail(f"{name} activity pair mismatch for {activity_name}: {pair!r}")

        client = by_kind["Client"]
        server = by_kind["Server"]
        require_tags(client, {"mcp.method.name": method, "network.transport": "pipe"})
        require_tags(server, {"mcp.method.name": method, "network.transport": "pipe"})
        require_tags(client, {"session.id": "qyl-mcp-evidence-session"})

        if client.get("TraceId") != root_trace_id or client.get("ParentSpanId") != root_span_id:
            fail(f"{name} client activity lost its qyl parent for {activity_name}: {client!r}")
        if server.get("TraceId") != client.get("TraceId") or server.get("ParentSpanId") != client.get("SpanId"):
            fail(f"{name} server activity did not continue client context for {activity_name}: {pair!r}")

        for kind, activity in by_kind.items():
            tags = activity["Tags"]
            session = tags.get("mcp.session.id")
            if not isinstance(session, str) or not session:
                fail(f"{name} {kind} activity is missing mcp.session.id: {activity!r}")
            sessions[kind].add(session)

    if any(len(values) != 1 for values in sessions.values()):
        fail(f"{name} session identifiers were not stable: {sessions!r}")
    if sessions["Client"] == sessions["Server"]:
        fail(f"{name} client/server sessions were not distinct: {sessions!r}")

    tool_pair = [
        activity for activity in activity_objects
        if activity.get("Name") == "tools/call qyl_sensitive_probe"
    ]
    for activity in tool_pair:
        require_tags(
            activity,
            {
                "gen_ai.tool.name": "qyl_sensitive_probe",
                "gen_ai.operation.name": "execute_tool",
                "mcp.protocol.version": "2025-11-25",
            },
        )


def publish_nativeaot(env: dict[str, str]) -> Path:
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
            "-p:TreatWarningsAsErrors=true",
            "--self-contained",
            "true",
            "--disable-build-servers",
            "-o",
            str(output),
            "-v",
            "quiet",
        ],
        ROOT,
        env,
    )
    executable = output / ("Qyl.RealMcpDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealMcpDemo")
    if not executable.exists():
        fail(f"NativeAOT MCP executable missing: {executable}")
    return executable


def run_nativeaot(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    run_checked(
        [
            "dotnet",
            "build",
            str(PROJECT),
            "-c",
            "Release",
            "-v",
            "quiet",
            "--disable-build-servers",
            "-m:1",
        ],
        ROOT,
        env,
    )
    managed = subprocess.run(
        ["dotnet", str(artifacts_bin_assembly(PROJECT))],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    verify_report("managed MCP demo", managed, "dynamic-code-supported", registration_enabled=True)

    disabled_env = dict(env)
    disabled_env["OTEL_DOTNET_AUTO_TRACES_MCP_INSTRUMENTATION_ENABLED"] = "false"
    managed_disabled = subprocess.run(
        ["dotnet", str(artifacts_bin_assembly(PROJECT))],
        cwd=PROJECT.parent,
        env=disabled_env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    verify_report(
        "managed MCP demo (registration disabled)",
        managed_disabled,
        "dynamic-code-supported",
        registration_enabled=False,
    )

    executable = publish_nativeaot(env)
    nativeaot = run_nativeaot(executable, env)
    verify_report("NativeAOT MCP demo", nativeaot, "nativeaot", registration_enabled=True)
    nativeaot_disabled = run_nativeaot(executable, disabled_env)
    verify_report(
        "NativeAOT MCP demo (registration disabled)",
        nativeaot_disabled,
        "nativeaot",
        registration_enabled=False,
    )
    print("real-mcp-demo-ok")


if __name__ == "__main__":
    main()
