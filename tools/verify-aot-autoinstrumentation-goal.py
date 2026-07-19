#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path

from verify_helpers import remove_publish_outputs


ROOT = Path(__file__).resolve().parents[1]
AOT_GATE_NAME = "nativeaot publish gate"


COMMANDS: list[tuple[str, list[str]]] = [
    ("contract invariants", [sys.executable, "tools/verify-contract-invariants.py"]),
    ("contract coverage report", [sys.executable, "tools/verify-contract-coverage-report.py"]),
    ("release build", ["dotnet", "build", "Qyl.OpenTelemetry.AutoInstrumentation.slnx", "-c", "Release"]),
    ("demos release build", ["dotnet", "build", "Qyl.OpenTelemetry.AutoInstrumentation.Demos.slnx", "-c", "Release"]),
    ("package layout", [sys.executable, "tools/verify-package-layout.py"]),
    ("projectreference behavior", [sys.executable, "tools/verify-projectreference-behavior.py"]),
    ("public api baseline", [sys.executable, "tools/verify-public-api-baseline.py"]),
    ("version sync", [sys.executable, "tools/verify-version-sync.py"]),
    ("xml doc enforcement", [sys.executable, "tools/verify-xml-doc-enforcement.py"]),
    ("environment options behavior", [sys.executable, "tools/verify-environment-options-behavior.py"]),
    ("instrumentation disabled behavior", [sys.executable, "tools/verify-instrumentation-disabled-behavior.py"]),
    ("generator snapshots", [sys.executable, "tools/verify-generator-snapshots.py"]),
    ("aspnetcore middleware delegate", [sys.executable, "tools/verify-aspnetcore-middleware-delegate.py"]),
    ("source interceptor consumer", [sys.executable, "tools/verify-source-interceptor-consumer.py"]),
    (AOT_GATE_NAME, []),
    ("real adonet demo", [sys.executable, "tools/verify-real-adonet-demo.py"]),
    ("real aspnetcore demo", [sys.executable, "tools/verify-real-aspnetcore-demo.py"]),
    ("real aspnetcore metrics demo", [sys.executable, "tools/verify-real-aspnetcore-metrics-demo.py"]),
    ("real azure demo", [sys.executable, "tools/verify-real-azure-demo.py"]),
    ("real corewcf demo", [sys.executable, "tools/verify-real-corewcf-demo.py"]),
    ("real efcore demo", [sys.executable, "tools/verify-real-efcore-demo.py"]),
    ("real elasticsearch demo", [sys.executable, "tools/verify-real-elasticsearch-demo.py"]),
    ("real elastictransport demo", [sys.executable, "tools/verify-real-elastictransport-demo.py"]),
    ("real genai demo", [sys.executable, "tools/verify-real-genai-demo.py"]),
    ("real graphql demo", [sys.executable, "tools/verify-real-graphql-demo.py"]),
    ("real grpc client demo", [sys.executable, "tools/verify-real-grpc-client-demo.py"]),
    ("real http client demo", [sys.executable, "tools/verify-real-http-client-demo.py"]),
    ("real ilogger demo", [sys.executable, "tools/verify-real-ilogger-demo.py"]),
    ("real kafka demo", [sys.executable, "tools/verify-real-kafka-demo.py"]),
    ("real log4net demo", [sys.executable, "tools/verify-real-log4net-demo.py"]),
    ("real masstransit demo", [sys.executable, "tools/verify-real-masstransit-demo.py"]),
    ("real mcp demo", [sys.executable, "tools/verify-real-mcp-demo.py"]),
    ("real mongodb demo", [sys.executable, "tools/verify-real-mongodb-demo.py"]),
    ("real mysqlconnector demo", [sys.executable, "tools/verify-real-mysqlconnector-demo.py"]),
    ("real mysqldata demo", [sys.executable, "tools/verify-real-mysqldata-demo.py"]),
    ("real netruntime metrics demo", [sys.executable, "tools/verify-real-netruntime-metrics-demo.py"]),
    ("real npgsql demo", [sys.executable, "tools/verify-real-npgsql-demo.py"]),
    ("real nlog demo", [sys.executable, "tools/verify-real-nlog-demo.py"]),
    ("real nservicebus demo", [sys.executable, "tools/verify-real-nservicebus-demo.py"]),
    ("real oraclemda demo", [sys.executable, "tools/verify-real-oraclemda-demo.py"]),
    ("real quartz demo", [sys.executable, "tools/verify-real-quartz-demo.py"]),
    ("real rabbitmq demo", [sys.executable, "tools/verify-real-rabbitmq-demo.py"]),
    ("real redis demo", [sys.executable, "tools/verify-real-redis-demo.py"]),
    ("real sqlclient demo", [sys.executable, "tools/verify-real-sqlclient-demo.py"]),
    ("real sqlite demo", [sys.executable, "tools/verify-real-sqlite-demo.py"]),
    ("real wcf client demo", [sys.executable, "tools/verify-real-wcf-client-demo.py"]),
    ("smoketest", ["bash", "tools/smoketest.sh"]),
    ("webapi aot demo", [sys.executable, "tools/verify-webapi-aot-demo.py"]),
    ("otlp receiver evidence", [sys.executable, "tools/verify-otlp-receiver.py"]),
    ("consumer behavior", [sys.executable, "tools/verify-consumer-behavior.py"]),
    ("nativeaot consumer verified", [sys.executable, "tools/verify-nativeaot-consumer.py"]),
    ("diff whitespace", ["git", "diff", "--check"]),
]


def parse_names(raw_names: list[str] | None) -> set[str]:
    if not raw_names:
        return set()

    names: set[str] = set()
    for raw_name in raw_names:
        names.update(name.strip() for name in raw_name.split(",") if name.strip())

    return names


def select_commands(only: set[str], skip: set[str]) -> list[tuple[str, list[str]]]:
    names = {name for name, _ in COMMANDS}
    unknown = (only | skip) - names
    if unknown:
        raise SystemExit(
            "unknown verifier name(s): "
            + ", ".join(sorted(unknown))
            + "\navailable: "
            + ", ".join(sorted(names))
        )

    selected = [(name, command) for name, command in COMMANDS if not only or name in only]
    if skip:
        selected = [(name, command) for name, command in selected if name not in skip]

    if not selected:
        raise SystemExit("no verifier commands selected")

    return selected


def main() -> None:
    parser = argparse.ArgumentParser(description="Run the qyl AOT auto-instrumentation handoff gate.")
    parser.add_argument(
        "--only",
        action="append",
        metavar="NAME[,NAME...]",
        help="Run only the named verifier command(s). Names are shown in the gate headers.",
    )
    parser.add_argument(
        "--skip",
        action="append",
        metavar="NAME[,NAME...]",
        help="Skip the named verifier command(s). Names are shown in the gate headers.",
    )
    parser.add_argument(
        "--list",
        action="store_true",
        help="Print available verifier command names and exit.",
    )
    parser.add_argument(
        "--no-demos",
        action="store_true",
        help='Skip the container-backed "real * demo" verifiers (the no-Docker validation floor).',
    )
    parser.add_argument(
        "--demos-only",
        action="store_true",
        help='Run only the container-backed "real * demo" verifiers.',
    )
    parser.add_argument(
        "--aot-set",
        choices=["clean", "warned", "all"],
        default="all",
        help="Select the central NativeAOT publish classification (always strict-promotion).",
    )
    parser.add_argument(
        "--keep-publish",
        action="store_true",
        help="Keep artifacts/publish after a successful run (default: removed — pure verification byproduct, multiple GB over the full demo matrix).",
    )
    args = parser.parse_args()

    if args.list:
        for name, _ in COMMANDS:
            print(name)
        return

    if args.no_demos and args.demos_only:
        raise SystemExit("--no-demos and --demos-only are mutually exclusive")
    demo_names = {name for name, _ in COMMANDS if name.startswith("real ")}
    only = parse_names(args.only)
    skip = parse_names(args.skip)
    if args.demos_only:
        only |= demo_names
    if args.no_demos:
        skip |= demo_names
    commands = select_commands(only, skip)
    full_gate = commands == COMMANDS
    commands = [
        (name, [sys.executable, "tools/verify-aot-publish-gate.py", "--set", args.aot_set,
                "--strict-promotion", "--keep-publish"] if name == AOT_GATE_NAME else command)
        for name, command in commands
    ]
    env = dict(os.environ)
    env["MSBUILDDISABLENODEREUSE"] = "1"
    for name, command in commands:
        print(f"== {name} ==")
        completed = subprocess.run(command, cwd=ROOT, env=env, check=False)
        if completed.returncode != 0:
            raise SystemExit(f"{name} failed with exit code {completed.returncode}")
        if name == AOT_GATE_NAME:
            env["AOT_PUBLISH_GATE_SET"] = args.aot_set

    # Only reached when every selected verifier passed; failures SystemExit above and
    # keep artifacts/publish around for inspection.
    if not args.keep_publish:
        print(remove_publish_outputs())

    if full_gate:
        print("aot-autoinstrumentation-goal-ok")
    else:
        print("aot-autoinstrumentation-goal-partial-ok selected=" + ",".join(name for name, _ in commands))


if __name__ == "__main__":
    main()
