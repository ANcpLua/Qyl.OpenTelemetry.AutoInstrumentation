#!/usr/bin/env python3
from __future__ import annotations

import argparse
import importlib.util
import json
from pathlib import Path
from types import ModuleType
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"


def fail(message: str) -> None:
    raise SystemExit(message)


def load_artifacts() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_contract_artifacts", ARTIFACTS_PATH)
    if spec is None or spec.loader is None:
        fail(f"cannot load contract artifact generator: {ARTIFACTS_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def build_report(artifacts: ModuleType, contract: dict[str, Any]) -> dict[str, Any]:
    artifacts.verify_contract_model(contract)
    items = artifacts.contract_items(contract)
    records = [
        {
            "index": int(item["index"]),
            "contract_item_id": str(item["contract_item_id"]),
            "kind": str(item["kind"]),
            "key": str(item["key"]),
            "lane": str(item["lane"]),
            "qyl_status": str(item["qyl_status"]),
            "call_site_visibility": str(item["call_site_visibility"]),
            "payload_access": str(item["payload_access"]),
            "evidence_level": str(item["evidence_level"]),
            "primary_owner": str(item["primary_owner"]),
        }
        for item in items
    ]
    return {
        "schema_id": "qyl-aot-autoinstrumentation-contract-coverage-report",
        "schema_version": "1.0.0",
        "counts": artifacts.contract_counts(contract),
        "items": records,
    }


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify and optionally emit the generated qyl contract coverage report.")
    parser.add_argument("--json", type=Path, help="Write the machine-readable report to this path.")
    parser.add_argument("--markdown", type=Path, help="Write the generated coverage matrix markdown to this path.")
    args = parser.parse_args()

    artifacts = load_artifacts()
    contract = artifacts.load_contract()
    report = build_report(artifacts, contract)
    artifacts.verify_generated_files(contract)

    if args.json is not None:
        args.json.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if args.markdown is not None:
        args.markdown.write_text(artifacts.render_coverage_matrix(contract), encoding="utf-8")

    counts = report["counts"]
    print(
        "contract-coverage-report-ok "
        f"total={counts['total_contract_items']} "
        f"signals={counts['signal_specific_instrumentation_promises']} "
        f"environment_controls={counts['global_environment_controls']} "
        f"instrumentation_options={counts['instrumentation_options']}"
    )


if __name__ == "__main__":
    main()
