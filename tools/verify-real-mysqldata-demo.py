#!/usr/bin/env python3
from __future__ import annotations

import json
import os
import platform
import subprocess
import sys
from pathlib import Path
from typing import Any

from verify_container_helpers import run_published_container
from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealMySqlDataDemo" / "Qyl.RealMySqlDataDemo.csproj"
TARGET_FRAMEWORK = "net10.0"
MYSQL_IMAGE = os.environ.get("QYL_MYSQL_IMAGE", "mysql:9")
MYSQL_PASSWORD = "qyl"
MYSQL_DATABASE = "qyl"

CONNECTOR_NET_CLIENT = "connector-net/Client"

# The exact span set MySql.Data 26.7.0 produces per operation, which is the assertion the demo
# makes and this gate restates. A physical open traces the driver's five handshake statements; the
# Connection activity it starts stops only at Close/Dispose, so it lands in the close phase. A
# pooled re-open traces nothing at open. Every user command is exactly one span.
EXPECTED_SPAN_SETS: dict[str, list[str]] = {
    "physical-open": [CONNECTOR_NET_CLIENT] * 5,
    "command-select": [CONNECTOR_NET_CLIENT],
    "command-failing-select": [CONNECTOR_NET_CLIENT],
    "close": [CONNECTOR_NET_CLIENT],
    "pooled-open": [],
    "pooled-close": [CONNECTOR_NET_CLIENT],
}
EXPECTED_ACTIVITY_COUNT = sum(len(tuples) for tuples in EXPECTED_SPAN_SETS.values())

# The only sources the demo's process-wide listener may drop. Everything else it sees is asserted,
# so a third instrumentation anywhere in the process fails the gate.
EXCLUDED_SOURCE_PREFIX = "Experimental."

# MySql.Data 26.7.0 publishes for NativeAOT and cannot run. The first Open() runs
# MySqlConfiguration's static constructor, which reads ConfigurationManager, and the config host it
# activates by name is gone from the trimmed application. Rooting
# System.Configuration.ConfigurationManager only moves the abort one layer deeper, into
# NativeDriver.OpenAsync, so this is the driver's own AOT gap and not a publish setting. The
# publish stays under the AOT gate -- its approved diagnostics are unchanged -- and the run is
# pinned to the exact abort below, so the lane fails and gets promoted the day the driver runs.
NATIVEAOT_ABORT_MARKERS = (
    "MySql.Data.MySqlClient.MySqlConfiguration..cctor()",
    "No parameterless constructor defined for type 'System.Configuration.ClientConfigurationHost'.",
)


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"MySql.Data demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"MySql.Data demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"MySql.Data demo report must be a JSON object: {report!r}")
    return report


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    expected_runtime_mode: str,
) -> dict[str, Any]:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    for token in [
        "physical-open=1",
        "select-scalar=1",
        "expected-mysqldata-error=MySql.Data.MySqlClient.MySqlException",
        "pooled-open=1",
    ]:
        if token not in completed.stdout:
            fail(f"{name} missing output token {token!r}\nstdout={completed.stdout}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    verify_span_sets(name, report)
    verify_excluded_sources(name, report)
    verify_status_code_rereadings(name, report)

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) != EXPECTED_ACTIVITY_COUNT:
        fail(f"{name} expected exactly {EXPECTED_ACTIVITY_COUNT} connector-net activities, got {activities!r}")
    return report


def verify_span_sets(name: str, report: dict[str, Any]) -> None:
    """Every operation's (scope, kind) multiset, restated here so the gate does not just echo the demo."""
    span_sets = report.get("SpanSets")
    if not isinstance(span_sets, list):
        fail(f"{name} report has no SpanSets: {report!r}")

    observed = {}
    for entry in span_sets:
        if not isinstance(entry, dict):
            fail(f"{name} SpanSets entry is not an object: {entry!r}")
        observed[entry.get("Phase")] = entry

    if set(observed) != set(EXPECTED_SPAN_SETS):
        fail(f"{name} phases: expected {sorted(EXPECTED_SPAN_SETS)}, got {sorted(observed)}")

    for phase, expected in EXPECTED_SPAN_SETS.items():
        entry = observed[phase]
        actual = entry.get("Actual")
        if sorted(expected) != actual:
            fail(f"{name} {phase}: expected span set {sorted(expected)}, got {actual!r}")
        if entry.get("Expected") != sorted(expected):
            fail(f"{name} {phase}: demo asserted {entry.get('Expected')!r}, gate expects {sorted(expected)}")


def verify_excluded_sources(name: str, report: dict[str, Any]) -> None:
    """The one exclusion rule: the runtime's own Experimental.* diagnostics, and nothing else."""
    excluded = report.get("ExcludedSources")
    if not isinstance(excluded, list) or not excluded:
        fail(f"{name} expected the demo to have seen and excluded Experimental.* sources, got {excluded!r}")
    for source in excluded:
        if not isinstance(source, str) or not source.startswith(EXCLUDED_SOURCE_PREFIX):
            fail(f"{name} excluded a source outside the {EXCLUDED_SOURCE_PREFIX}* rule: {source!r}")


def verify_status_code_rereadings(name: str, report: dict[str, Any]) -> None:
    """
    MySql.Data keeps writing to a stopped activity.

    The failing command's span reads ``otel.status_code=ERROR`` at the instant a span processor
    sees it, and ``OK`` once the driver is done with it. Both readings are asserted, because a
    simple export processor records the first and a batching one records the second.
    """
    rereadings = report.get("StatusCodeRereadings")
    if not isinstance(rereadings, list) or len(rereadings) != EXPECTED_ACTIVITY_COUNT:
        fail(f"{name} expected {EXPECTED_ACTIVITY_COUNT} status-code re-readings, got {rereadings!r}")

    failing = [entry for entry in rereadings if entry.get("Phase") == "command-failing-select"]
    if len(failing) != 1:
        fail(f"{name} expected 1 re-reading for the failing command, got {failing!r}")
    if failing[0].get("AtStop") != "ERROR" or failing[0].get("AfterStop") != "OK":
        fail(f"{name} failing command status codes changed shape: {failing[0]!r}")

    for entry in rereadings:
        if entry.get("Phase") == "command-failing-select":
            continue
        if entry.get("AtStop") != "OK" or entry.get("AfterStop") != "OK":
            fail(f"{name} expected OK/OK on a succeeding span, got {entry!r}")


def verify_statement_capture_is_unconditional(default: dict[str, Any], optin: dict[str, Any]) -> None:
    """
    ``db.statement`` carries the full command text with no opt-in.

    MySql.Data has no consumer switch for it -- ``MySql.Data.OpenTelemetry``'s ``AddConnectorNet()``
    is ``AddSource("connector-net")`` and nothing else -- and ``OTEL_SEMCONV_STABILITY_OPT_IN``
    reaches no code of the driver's. The proof is that the two reports are identical.
    """

    def shape(report: dict[str, Any]) -> list[tuple[str, str, str, tuple[str, ...], str]]:
        return [
            (
                activity["Phase"],
                activity["Name"],
                activity["Kind"],
                tuple(sorted(activity["Tags"])),
                activity["Tags"].get("db.statement", {}).get("Value", ""),
            )
            for activity in report["Activities"]
        ]

    if shape(default) != shape(optin):
        fail(
            "OTEL_SEMCONV_STABILITY_OPT_IN changed the MySql.Data span shape:\n"
            f"default={json.dumps(shape(default), indent=2)}\n"
            f"opt-in={json.dumps(shape(optin), indent=2)}"
        )

    statements = {
        activity["Phase"]: activity["Tags"]["db.statement"]["Value"]
        for activity in default["Activities"]
        if "db.statement" in activity["Tags"]
    }
    if statements.get("command-select") != "SELECT 1 AS qyl_probe_value":
        fail(f"db.statement is not the full command text: {statements!r}")
    if statements.get("command-failing-select") != "SELECT qyl_missing_column FROM qyl_missing_table":
        fail(f"db.statement is not the full command text on the failing command: {statements!r}")


def verify_nativeaot_limitation(completed: subprocess.CompletedProcess[str]) -> None:
    """The NativeAOT binary aborts inside MySql.Data, and the gate pins where."""
    if completed.returncode == 0:
        fail(
            "the NativeAOT MySql.Data demo now runs: replace this pinned limitation with the full "
            "verify_report() assertion the managed lanes use"
        )

    output = completed.stdout + completed.stderr
    for marker in NATIVEAOT_ABORT_MARKERS:
        if marker not in output:
            fail(
                f"the NativeAOT MySql.Data demo failed somewhere new -- {marker!r} is missing\n"
                f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
            )

    print("nativeaot MySql.Data run pinned to MySqlConfiguration..cctor / ConfigurationManager")


def build_managed(env: dict[str, str]) -> None:
    run_checked(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)


def run_managed(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    assembly = artifacts_bin_assembly(PROJECT)
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
        env=env,
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
    executable = output / ("Qyl.RealMySqlDataDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealMySqlDataDemo")
    if not executable.exists():
        fail(f"NativeAOT MySql.Data executable missing: {executable}")
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
    build_managed(env)
    executable = publish_nativeaot(env)

    with run_published_container(
        cwd=ROOT,
        env=env,
        name_prefix="mysqldata",
        image=MYSQL_IMAGE,
        container_port=3306,
        container_env={"MYSQL_ROOT_PASSWORD": MYSQL_PASSWORD, "MYSQL_DATABASE": MYSQL_DATABASE},
        timeout_seconds=120,
    ) as mysql:
        env["QYL_MYSQL_CONNECTION_STRING"] = (
            f"Server={mysql.host};Port={mysql.port};User ID=root;Password={MYSQL_PASSWORD};Database={MYSQL_DATABASE};"
        )
        managed = run_managed(env)

        # The driver reads no stability opt-in: the same run with the variable set has to produce
        # the same spans, byte for byte.
        optin_env = dict(env)
        optin_env["OTEL_SEMCONV_STABILITY_OPT_IN"] = "database"
        optin = run_managed(optin_env)

        nativeaot = run_nativeaot(executable, env)

    managed_report = verify_report("managed MySql.Data demo", managed, "dynamic-code-supported")
    optin_report = verify_report("managed MySql.Data demo (stability opt-in)", optin, "dynamic-code-supported")
    verify_statement_capture_is_unconditional(managed_report, optin_report)
    verify_nativeaot_limitation(nativeaot)
    print("real-mysqldata-demo-ok")


if __name__ == "__main__":
    main()
