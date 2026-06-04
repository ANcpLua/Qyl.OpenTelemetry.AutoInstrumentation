#!/usr/bin/env python3
from __future__ import annotations

import argparse
import importlib.util
import json
import re
from pathlib import Path
from types import ModuleType
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
VERIFIER_PATH = ROOT / "tools" / "verify-contract-invariants.py"


def fail(message: str) -> None:
    raise SystemExit(message)


def load_verifier() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_contract_invariants", VERIFIER_PATH)
    if spec is None or spec.loader is None:
        fail(f"cannot load verifier module: {VERIFIER_PATH}")

    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def is_environment_variable_bound(options: str, variable: str) -> bool:
    if "{0}" not in variable:
        return variable in options

    prefix, suffix = variable.split("{0}", 1)
    return f'"{prefix}" + instrumentationId + "{suffix}"' in options


def build_report(verifier: ModuleType) -> dict[str, Any]:
    yaml_signal_keys, unsupported_keys = verifier.verify_yaml_vs_contract()
    verifier.verify_generator_keys(yaml_signal_keys, unsupported_keys)
    verifier.verify_environment_contract()
    verifier.verify_semconv_attribute_contract()
    verifier.verify_behavior_semantics_contract()
    verifier.verify_productive_mechanism_contract()

    items = verifier.parse_yaml_items()
    generator = verifier.GENERATOR_PATH.read_text()
    options = verifier.OPTIONS_PATH.read_text()
    generator_keys = set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', generator))
    source_generated_keys = yaml_signal_keys - unsupported_keys

    records: list[dict[str, Any]] = []
    for item in sorted(items, key=lambda candidate: int(candidate["index"])):
        kind = str(item["kind"])
        key = str(item["key"])
        evidence: list[str] = []

        if kind == "signal_specific_instrumentation_promise":
            if key in unsupported_keys:
                status = "unsupported_nativeaot_parity_or_dynamic_signal"
                evidence.append("InstrumentationContract.UnsupportedNativeAotSignalKeys")
            elif key in source_generated_keys and key in generator_keys:
                status = "source_generated_signal"
                evidence.append("QylAutoInstrumentationGenerator.InterceptorTarget.ContractKey")
                evidence.append("InstrumentationContract.TryGetSourceGeneratedSignal")
            else:
                status = "missing_signal_binding"
        elif kind == "global_environment_control":
            variable = str(item["environment_variable"])
            if is_environment_variable_bound(options, variable):
                status = "runtime_environment_control"
                evidence.append("QylAutoInstrumentationOptions")
            else:
                status = "missing_environment_control"
        elif kind == "instrumentation_option":
            variable = str(item["environment_variable"])
            if variable in options:
                status = "runtime_instrumentation_option"
                evidence.append("QylAutoInstrumentationOptions")
            else:
                status = "missing_instrumentation_option"
        else:
            status = "unknown_contract_kind"

        records.append(
            {
                "index": int(item["index"]),
                "contract_item_id": f"contract.item.{int(item['index']):02d}",
                "kind": kind,
                "key": key,
                "status": status,
                "evidence": evidence,
            }
        )

    counts = {
        "total": len(records),
        "source_generated_signals": sum(1 for record in records if record["status"] == "source_generated_signal"),
        "unsupported_signals": sum(1 for record in records if record["status"] == "unsupported_nativeaot_parity_or_dynamic_signal"),
        "environment_controls": sum(1 for record in records if record["status"] == "runtime_environment_control"),
        "instrumentation_options": sum(1 for record in records if record["status"] == "runtime_instrumentation_option"),
        "missing": sum(1 for record in records if str(record["status"]).startswith("missing_")),
    }
    expected = {
        "total": 60,
        "source_generated_signals": 33,
        "unsupported_signals": 4,
        "environment_controls": 7,
        "instrumentation_options": 16,
        "missing": 0,
    }
    if counts != expected:
        fail(f"coverage report counts mismatch: expected={expected} actual={counts}")

    return {
        "schema_id": "qyl-aot-autoinstrumentation-contract-coverage-report",
        "schema_version": "1.0.0",
        "counts": counts,
        "items": records,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify and optionally emit the 60-item qyl contract coverage report.")
    parser.add_argument("--json", type=Path, help="Write the full machine-readable report to this path.")
    args = parser.parse_args()

    report = build_report(load_verifier())
    if args.json is not None:
        args.json.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")

    counts = report["counts"]
    print(
        "contract-coverage-report-ok "
        f"total={counts['total']} "
        f"source_generated_signals={counts['source_generated_signals']} "
        f"unsupported_signals={counts['unsupported_signals']} "
        f"environment_controls={counts['environment_controls']} "
        f"instrumentation_options={counts['instrumentation_options']}"
    )


if __name__ == "__main__":
    main()
