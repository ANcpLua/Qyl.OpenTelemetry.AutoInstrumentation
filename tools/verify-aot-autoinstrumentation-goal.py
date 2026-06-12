#!/usr/bin/env python3
from __future__ import annotations

import argparse
import os
import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


COMMANDS: list[tuple[str, list[str]]] = [
    ("contract invariants", [sys.executable, "tools/verify-contract-invariants.py"]),
    ("contract coverage report", [sys.executable, "tools/verify-contract-coverage-report.py"]),
    ("release build", ["dotnet", "build", "Qyl.AutoInstrumentation.slnx", "-c", "Release"]),
    ("package layout", [sys.executable, "tools/verify-package-layout.py"]),
    ("projectreference behavior", [sys.executable, "tools/verify-projectreference-behavior.py"]),
    ("public api baseline", [sys.executable, "tools/verify-public-api-baseline.py"]),
    ("xml doc enforcement", [sys.executable, "tools/verify-xml-doc-enforcement.py"]),
    ("environment options behavior", [sys.executable, "tools/verify-environment-options-behavior.py"]),
    ("conformance opt-in", [sys.executable, "tools/verify-conformance-opt-in.py"]),
    ("generator snapshots", [sys.executable, "tools/verify-generator-snapshots.py"]),
    ("source interceptor consumer", [sys.executable, "tools/verify-source-interceptor-consumer.py"]),
    ("smoketest", ["bash", "tools/smoketest.sh"]),
    ("webapi aot demo", [sys.executable, "tools/verify-webapi-aot-demo.py"]),
    ("otlp verified fixtures", [sys.executable, "tools/verify-otlp-fixtures.py"]),
    ("otlp collector fixtures", [sys.executable, "tools/verify-otlp-collector-fixtures.py"]),
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
    args = parser.parse_args()

    commands = select_commands(parse_names(args.only), parse_names(args.skip))
    env = dict(os.environ)
    env["MSBUILDDISABLENODEREUSE"] = "1"
    for name, command in commands:
        print(f"== {name} ==")
        completed = subprocess.run(command, cwd=ROOT, env=env, check=False)
        if completed.returncode != 0:
            raise SystemExit(f"{name} failed with exit code {completed.returncode}")

    if commands == COMMANDS:
        print("aot-autoinstrumentation-goal-ok")
    else:
        print("aot-autoinstrumentation-goal-partial-ok selected=" + ",".join(name for name, _ in commands))


if __name__ == "__main__":
    main()
