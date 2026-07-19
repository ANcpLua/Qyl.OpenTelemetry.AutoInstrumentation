#!/usr/bin/env python3
from __future__ import annotations

import json
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import artifacts_bin_assembly, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealGenAiDemo" / "Qyl.RealGenAiDemo.csproj"
GENAI_SOURCES = {
    "Experimental.Microsoft.Extensions.AI",
    "Experimental.Microsoft.Agents.AI",
    "Microsoft.Agents.AI.Workflows",
}
GENAI_METERS = {
    "Experimental.Microsoft.Agents.AI",
    "Experimental.Microsoft.Extensions.AI",
}


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"GenAI demo did not emit a JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"GenAI demo emitted an invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"GenAI demo report must be a JSON object: {report!r}")
    return report


def verify_report(
    name: str,
    completed: subprocess.CompletedProcess[str],
    *,
    expected_qyl_sources: set[str],
    expected_qyl_meters: set[str],
) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != "dynamic-code-supported":
        fail(f"managed GenAI demo runtime mode mismatch: {report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"managed GenAI demo report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    for field in ("BareResponseVerified", "AgentResponseVerified", "WorkflowOutputVerified"):
        if report.get(field) is not True:
            fail(f"managed GenAI demo did not verify {field}: {report!r}")

    activities = report.get("Activities")
    if not isinstance(activities, list):
        fail(f"managed GenAI demo Activities must be a list: {activities!r}")
    source_names = {activity.get("SourceName") for activity in activities if isinstance(activity, dict)}
    if source_names != GENAI_SOURCES:
        fail(f"{name} source mismatch: expected={GENAI_SOURCES!r} actual={source_names!r}")

    meter_names = report.get("PublishedMeterNames")
    if meter_names != sorted(GENAI_METERS):
        fail(f"{name} meter mismatch: expected={sorted(GENAI_METERS)!r} actual={meter_names!r}")

    measurements = report.get("Measurements")
    if not isinstance(measurements, list) or len(measurements) != 14:
        fail(f"managed GenAI demo expected exactly 14 measurements, got {measurements!r}")

    qyl_sources = report.get("QylExportedActivitySources")
    if qyl_sources != sorted(expected_qyl_sources):
        fail(f"{name} Qyl.Sdk exported source mismatch: expected={sorted(expected_qyl_sources)!r} actual={qyl_sources!r}")

    qyl_meters = report.get("QylExportedMeterNames")
    if not isinstance(qyl_meters, list):
        fail(f"{name} Qyl.Sdk exported meters must be a list: {qyl_meters!r}")
    actual_qyl_meters = set(qyl_meters) & GENAI_METERS
    if actual_qyl_meters != expected_qyl_meters:
        fail(f"{name} Qyl.Sdk exported meter mismatch: expected={sorted(expected_qyl_meters)!r} actual={qyl_meters!r}")


def run_demo(env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["dotnet", str(artifacts_bin_assembly(PROJECT))],
        cwd=PROJECT.parent,
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
    verify_report(
        "managed GenAI demo",
        run_demo(env),
        expected_qyl_sources=GENAI_SOURCES,
        expected_qyl_meters=GENAI_METERS,
    )

    scenarios = [
        (
            "MEAI traces disabled",
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED",
            GENAI_SOURCES - {"Experimental.Microsoft.Extensions.AI"},
            GENAI_METERS,
        ),
        (
            "MEAI metrics disabled",
            "OTEL_DOTNET_AUTO_METRICS_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED",
            GENAI_SOURCES,
            GENAI_METERS - {"Experimental.Microsoft.Extensions.AI"},
        ),
        (
            "Agents traces disabled",
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED",
            GENAI_SOURCES - {"Experimental.Microsoft.Agents.AI"},
            GENAI_METERS,
        ),
        (
            "Agents metrics disabled",
            "OTEL_DOTNET_AUTO_METRICS_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED",
            GENAI_SOURCES,
            GENAI_METERS - {"Experimental.Microsoft.Agents.AI"},
        ),
        (
            "Workflows traces disabled",
            "OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAIWORKFLOWS_INSTRUMENTATION_ENABLED",
            GENAI_SOURCES - {"Microsoft.Agents.AI.Workflows"},
            GENAI_METERS,
        ),
    ]
    for name, variable, expected_sources, expected_meters in scenarios:
        scenario_env = dict(env)
        scenario_env[variable] = "false"
        verify_report(
            f"managed GenAI demo ({name})",
            run_demo(scenario_env),
            expected_qyl_sources=expected_sources,
            expected_qyl_meters=expected_meters,
        )

    all_disabled_env = dict(env)
    for _, variable, _, _ in scenarios:
        all_disabled_env[variable] = "false"
    verify_report(
        "managed GenAI demo (all registrations disabled)",
        run_demo(all_disabled_env),
        expected_qyl_sources=set(),
        expected_qyl_meters=set(),
    )
    print("real-genai-demo-ok")


if __name__ == "__main__":
    main()
