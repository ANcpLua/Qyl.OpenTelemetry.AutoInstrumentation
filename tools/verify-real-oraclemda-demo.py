#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
import time
from pathlib import Path
from typing import Any

from verify_container_helpers import PublishedContainer, run_published_container
from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealOracleMdaDemo" / "Qyl.RealOracleMdaDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
ORACLE_IMAGE = os.environ.get("QYL_ORACLE_IMAGE", "gvenzl/oracle-free:23-slim-faststart")
ORACLE_PASSWORD = "qyl"
ORACLE_SERVICE = "FREEPDB1"
ORACLE_READY_MARKER = "DATABASE IS READY TO USE"
# A cold pull plus first start of the free database image; the faststart tag is far quicker, but
# the bound has to cover the cold case rather than flake on it.
ORACLE_READY_TIMEOUT_SECONDS = 900

ORACLE_CLIENT = "Oracle.ManagedDataAccess.Core/Client"

MODE_NONE = "none"
MODE_ADDON_DEFAULT = "addon-default"
MODE_ADDON_ENRICHED = "addon-enriched"
MODES = (MODE_NONE, MODE_ADDON_DEFAULT, MODE_ADDON_ENRICHED)

# The exact span set ODP.NET 23.26.300 produces per operation. Open and Close trace nothing until
# the vendor add-on's open/close tracing is turned on; a command is always the verb's own span and
# the SendExecuteRequest beneath it.
COMMAND_PHASES = ("command-scalar", "command-nonquery", "command-failing")
CONNECTION_PHASES = ("open", "close")


def expected_span_sets(mode: str) -> dict[str, list[str]]:
    connection_tuples = [ORACLE_CLIENT, ORACLE_CLIENT] if mode == MODE_ADDON_ENRICHED else []
    span_sets = {phase: [ORACLE_CLIENT, ORACLE_CLIENT] for phase in COMMAND_PHASES}
    span_sets.update({phase: list(connection_tuples) for phase in CONNECTION_PHASES})
    return span_sets


# The four keys ODP.NET writes on its own, plus the one the qyl processor stamps.
DEFAULT_VOCABULARY = [
    "db.odp.roundtrip.count",
    "db.odp.roundtrip.duration",
    "db.response.returned_rows",
    "db.system",
    "qyl.instrumentation.domain",
]

# What Oracle.ManagedDataAccess.OpenTelemetry adds once its enrichment options are on. The package
# is enrichment only: none of these gate whether a span exists.
ENRICHED_ONLY_KEYS = [
    "db.name",
    "db.odp.connection.id",
    "db.odp.sql_id",
    "db.statement",
    "db.user",
    "server.address",
    "server.port",
]

# The only sources the demo's process-wide listener may drop.
EXCLUDED_SOURCE_PREFIX = "Experimental."


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"Oracle MDA demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"Oracle MDA demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"Oracle MDA demo report must be a JSON object: {report!r}")
    return report


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    expected_runtime_mode: str,
    mode: str,
) -> dict[str, Any]:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        f"oracle-mode={mode}",
        # OracleConfiguration.OpenTelemetryTracing defaults to true, which is why AddSource alone
        # delivers spans.
        "oracle-opentelemetry-tracing=True",
        "oracle-open=1",
        "scalar-value=1",
        "nonquery-rows=-1",
        "expected-oraclemda-error=Oracle.ManagedDataAccess.Client.OracleException",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("AddonMode") != mode:
        fail(f"{name} add-on mode mismatch: expected={mode} actual={report.get('AddonMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    verify_span_sets(name, report, mode)
    verify_excluded_sources(name, report)
    verify_vocabulary(name, report, mode)

    expected_count = sum(len(tuples) for tuples in expected_span_sets(mode).values())
    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != expected_count:
        fail(f"{name} expected exactly {expected_count} ODP.NET activities, got {activities!r}")
    return report


def verify_span_sets(name: str, report: dict[str, Any], mode: str) -> None:
    """Every operation's (scope, kind) multiset, restated here so the gate does not just echo the demo."""
    expected_sets = expected_span_sets(mode)
    span_sets = report.get("SpanSets")
    if not isinstance(span_sets, list):
        fail(f"{name} report has no SpanSets: {report!r}")

    observed = {}
    for entry in span_sets:
        if not isinstance(entry, dict):
            fail(f"{name} SpanSets entry is not an object: {entry!r}")
        observed[entry.get("Phase")] = entry

    if set(observed) != set(expected_sets):
        fail(f"{name} phases: expected {sorted(expected_sets)}, got {sorted(observed)}")

    for phase, expected in expected_sets.items():
        entry = observed[phase]
        if entry.get("Actual") != sorted(expected):
            fail(f"{name} {phase}: expected span set {sorted(expected)}, got {entry.get('Actual')!r}")
        if entry.get("Expected") != sorted(expected):
            fail(f"{name} {phase}: demo asserted {entry.get('Expected')!r}, gate expects {sorted(expected)}")

    # A command is a parent and its one child, not two siblings.
    roots = {}
    for activity in report["Activities"]:
        if activity["ParentSpanId"] == "0" * 16:
            roots[activity["Phase"]] = activity["SpanId"]
    for phase in COMMAND_PHASES:
        if phase not in roots:
            fail(f"{name} {phase} produced no root span")
        children = [
            activity for activity in report["Activities"]
            if activity["Phase"] == phase and activity["ParentSpanId"] == roots[phase]
        ]
        if len(children) != 1:
            fail(f"{name} {phase} expected exactly 1 child of its root span, got {len(children)}")


def verify_excluded_sources(name: str, report: dict[str, Any]) -> None:
    """The one exclusion rule: the runtime's own Experimental.* diagnostics, and nothing else."""
    excluded = report.get("ExcludedSources")
    if not isinstance(excluded, list) or not excluded:
        fail(f"{name} expected the demo to have seen and excluded Experimental.* sources, got {excluded!r}")
    for source in excluded:
        if not isinstance(source, str) or not source.startswith(EXCLUDED_SOURCE_PREFIX):
            fail(f"{name} excluded a source outside the {EXCLUDED_SOURCE_PREFIX}* rule: {source!r}")


def verify_vocabulary(name: str, report: dict[str, Any], mode: str) -> None:
    expected = sorted(DEFAULT_VOCABULARY + ENRICHED_ONLY_KEYS) if mode == MODE_ADDON_ENRICHED else sorted(DEFAULT_VOCABULARY)
    if report.get("TagVocabulary") != expected:
        fail(f"{name} tag vocabulary: expected {expected}, got {report.get('TagVocabulary')!r}")


def verify_addon_delta(reports: dict[str, dict[str, Any]]) -> None:
    """
    The add-on is enrichment, and only once its options are on.

    Registering ``AddOracleDataProviderInstrumentation()`` with nothing configured leaves the spans
    exactly as ODP.NET emits them: every option that adds an attribute is off by default. Turning
    them on adds seven attribute keys and the connection spans, and takes none away.
    """

    def shape(report: dict[str, Any]) -> list[tuple[str, str, str, tuple[str, ...]]]:
        return sorted(
            (activity["Phase"], activity["Name"], activity["Status"], tuple(sorted(activity["Tags"])))
            for activity in report["Activities"]
        )

    if shape(reports[MODE_NONE]) != shape(reports[MODE_ADDON_DEFAULT]):
        fail(
            "AddOracleDataProviderInstrumentation() with no options changed the spans:\n"
            f"none={json.dumps(shape(reports[MODE_NONE]), indent=2)}\n"
            f"addon-default={json.dumps(shape(reports[MODE_ADDON_DEFAULT]), indent=2)}"
        )

    default_keys = set(reports[MODE_NONE]["TagVocabulary"])
    enriched_keys = set(reports[MODE_ADDON_ENRICHED]["TagVocabulary"])
    if sorted(enriched_keys - default_keys) != sorted(ENRICHED_ONLY_KEYS):
        fail(f"add-on attribute delta: expected {sorted(ENRICHED_ONLY_KEYS)}, got {sorted(enriched_keys - default_keys)}")
    if default_keys - enriched_keys:
        fail(f"the add-on removed attributes ODP.NET writes on its own: {sorted(default_keys - enriched_keys)}")

    # The failing command: a bare status code without the add-on, an ORA description and an
    # exception event with RecordException on.
    def failing_root(report: dict[str, Any]) -> dict[str, Any]:
        for activity in report["Activities"]:
            if activity["Phase"] == "command-failing" and activity["ParentSpanId"] == "0" * 16:
                return activity
        fail(f"no failing root span in {report.get('AddonMode')!r} run")
        raise AssertionError

    bare = failing_root(reports[MODE_NONE])
    enriched = failing_root(reports[MODE_ADDON_ENRICHED])
    if bare["Status"] != "Error" or bare["StatusDescription"] is not None or bare["Events"]:
        fail(f"the default failing root is no longer a bare Error: {bare!r}")
    if enriched["Status"] != "Error" or not enriched["StatusDescription"]:
        fail(f"the enriched failing root has no status description: {enriched!r}")
    if [event["Name"] for event in enriched["Events"]] != ["exception"]:
        fail(f"the enriched failing root has no single exception event: {enriched['Events']!r}")

    # AddDBInfoToDisplayName rewrites the span names to "<verb> HOST:PORT:DATABASE".
    for activity in reports[MODE_ADDON_ENRICHED]["Activities"]:
        suffix = f"{activity['Tags']['server.address']['Value']}:{activity['Tags']['server.port']['Value']}:{activity['Tags']['db.name']['Value']}"
        if not activity["Name"].endswith(" " + suffix):
            fail(f"enriched display name does not carry the database info: {activity['Name']!r}")


def build_managed(env: dict[str, str]) -> None:
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)


def run_managed(env: dict[str, str], mode: str) -> subprocess.CompletedProcess[str]:
    lane_env = dict(env)
    lane_env["QYL_ORACLE_ADDON_MODE"] = mode
    return subprocess.run(
        ["dotnet", str(artifacts_bin_assembly(PROJECT))],
        cwd=PROJECT.parent,
        env=lane_env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def publish_nativeaot(env: dict[str, str]) -> Path:
    if env.get("AOT_PUBLISH_GATE_SET") not in {"warned", "all"}:
        run_checked(
            [sys.executable, "tools/verify-aot-publish-gate.py", "--set", "warned", "--demo",
             PROJECT.stem, "--strict-promotion", "--keep-publish"], ROOT, env)

    output = artifacts_publish_dir(PROJECT, "nativeaot")
    executable = output / ("Qyl.RealOracleMdaDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealOracleMdaDemo")
    if not executable.exists():
        fail(f"NativeAOT Oracle MDA executable missing: {executable}")
    return executable


def run_nativeaot(executable: Path, env: dict[str, str], mode: str) -> subprocess.CompletedProcess[str]:
    lane_env = dict(env)
    lane_env["QYL_ORACLE_ADDON_MODE"] = mode
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=lane_env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def wait_for_database(container: PublishedContainer, env: dict[str, str]) -> None:
    """The published port answers long before the database does; the log line is the real signal."""
    deadline = time.monotonic() + ORACLE_READY_TIMEOUT_SECONDS
    while time.monotonic() < deadline:
        logs = subprocess.run(
            ["docker", "logs", container.name],
            cwd=ROOT,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.STDOUT,
            check=False,
        )
        if ORACLE_READY_MARKER in logs.stdout:
            return
        time.sleep(2)

    fail(f"{container.name} did not report {ORACLE_READY_MARKER!r} within {ORACLE_READY_TIMEOUT_SECONDS}s")


def main() -> None:
    env = clean_env()
    build_managed(env)
    executable = publish_nativeaot(env)

    managed_reports: dict[str, dict[str, Any]] = {}
    nativeaot_runs: dict[str, subprocess.CompletedProcess[str]] = {}
    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="oraclemda",
        image=ORACLE_IMAGE,
        container_port=1521,
        container_env={"ORACLE_PASSWORD": ORACLE_PASSWORD},
        timeout_seconds=120,
    ) as oracle:
        wait_for_database(oracle, env)
        env["QYL_ORACLE_CONNECTION_STRING"] = (
            f"User Id=system;Password={ORACLE_PASSWORD};Data Source={oracle.host}:{oracle.port}/{ORACLE_SERVICE};"
        )
        managed_runs = {mode: run_managed(env, mode) for mode in MODES}
        nativeaot_runs = {mode: run_nativeaot(executable, env, mode) for mode in MODES}

    for mode in MODES:
        managed_reports[mode] = verify_report(
            f"managed Oracle MDA demo ({mode})", managed_runs[mode], "dynamic-code-supported", mode)
        verify_report(
            f"NativeAOT Oracle MDA demo ({mode})", nativeaot_runs[mode], "nativeaot", mode)

    verify_addon_delta(managed_reports)
    print("real-oraclemda-demo-ok")


if __name__ == "__main__":
    main()
