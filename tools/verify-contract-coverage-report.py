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
COVERAGE_MATRIX_PATH = ROOT / "docs" / "coverage-matrix.md"


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


def parse_dispatch_emitters(generator: str) -> dict[str, str]:
    try:
        dispatch_block = generator.split("for (var index = 0; index < invocations.Length; index++)", 1)[1].split(
            'context.AddSource("QylAutoInstrumentation.Interceptors.g.cs"', 1)[0]
    except IndexError:
        fail("EmitInterceptors dispatch block missing")

    emitters: dict[str, str] = {}
    for match in re.finditer(
        r"invocation\.Target\.Kind is ([^\n]+)\)\s*\n\s*(Emit[A-Za-z0-9]+Interceptor)\(",
        dispatch_block,
    ):
        emitter = match.group(2)
        for kind in re.findall(r"InterceptorKind\.([A-Za-z0-9]+)", match.group(1)):
            emitters[kind] = emitter

    if "EmitDbCommandInterceptor(builder, invocation, index);" in dispatch_block:
        emitters["DbCommand"] = "EmitDbCommandInterceptor"

    return emitters


def add_signal_evidence(
    evidence_by_signal: dict[str, set[str]],
    signal_key: str,
    kind: str,
    emitters_by_kind: dict[str, str],
) -> None:
    evidence = evidence_by_signal.setdefault(signal_key, set())
    evidence.add(f"QylAutoInstrumentationGenerator.InterceptorKind.{kind}")
    if kind in emitters_by_kind:
        evidence.add(f"QylAutoInstrumentationGenerator.{emitters_by_kind[kind]}")


def parse_target_signal_evidence(generator: str) -> dict[str, set[str]]:
    emitters_by_kind = parse_dispatch_emitters(generator)
    evidence_by_signal: dict[str, set[str]] = {}

    for match in re.finditer(r"new InterceptorTarget\((.*?)\);", generator, re.DOTALL):
        block = match.group(1)
        kind_match = re.search(r"InterceptorKind\.([A-Za-z0-9]+)", block)
        if kind_match is None:
            continue

        kind = kind_match.group(1)
        for signal_key in re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', block):
            add_signal_evidence(evidence_by_signal, signal_key, kind, emitters_by_kind)

    try:
        http_target = generator.split("private static InterceptorTarget HttpTarget", 1)[1].split(
            "private static string GetDbTraceContractKey", 1
        )[0]
    except IndexError:
        fail("HttpTarget helper missing from generator")

    for signal_key in re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', http_target):
        add_signal_evidence(evidence_by_signal, signal_key, "HttpClient", emitters_by_kind)

    for helper_name in ["GetDbTraceContractKey", "GetDbMetricContractKeys"]:
        db_keys = verifier_parse_switch_helper_keys(generator, helper_name)
        for signal_key in db_keys:
            add_signal_evidence(evidence_by_signal, signal_key, "DbCommand", emitters_by_kind)

    return evidence_by_signal


def verifier_parse_switch_helper_keys(generator: str, helper_name: str) -> set[str]:
    match = re.search(
        rf"private static [^=]+ {re.escape(helper_name)}\([^)]*\)\s*=>.*?}};",
        generator,
        re.DOTALL,
    )
    if match is None:
        fail(f"{helper_name} helper missing from generator")

    return set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', match.group(0)))


def build_report(verifier: ModuleType) -> dict[str, Any]:
    yaml_signal_keys, unsupported_keys = verifier.verify_yaml_vs_contract()
    verifier.verify_generator_keys(yaml_signal_keys, unsupported_keys)
    verifier.verify_environment_contract()
    verifier.verify_semconv_attribute_contract()
    verifier.verify_metric_contract()
    verifier.verify_behavior_semantics_contract()
    verifier.verify_productive_mechanism_contract()

    items = verifier.parse_yaml_items()
    generator = verifier.GENERATOR_PATH.read_text()
    options = verifier.OPTIONS_PATH.read_text()
    target_evidence_by_signal = parse_target_signal_evidence(generator)
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
            elif key in source_generated_keys and key in target_evidence_by_signal:
                status = "source_generated_signal"
                evidence.append("InstrumentationContract.TryGetSourceGeneratedSignal")
                evidence.extend(sorted(target_evidence_by_signal[key]))
            else:
                status = "missing_signal_binding"
        elif kind == "global_environment_control":
            variable = str(item["environment_variable"])
            if is_environment_variable_bound(options, variable):
                status = "runtime_environment_control"
                evidence.append("QylAutoInstrumentationOptions")
                evidence.append("tools/verify-environment-options-behavior.py")
            else:
                status = "missing_environment_control"
        elif kind == "instrumentation_option":
            variable = str(item["environment_variable"])
            if variable in options:
                status = "runtime_instrumentation_option"
                evidence.append("QylAutoInstrumentationOptions")
                evidence.append("tools/verify-environment-options-behavior.py")
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


def render_markdown(report: dict[str, Any]) -> str:
    counts = report["counts"]
    lines = [
        "# AOT Interceptor Coverage Matrix",
        "",
        "This matrix is generated from `docs/otel-dotnet-auto-60-contract-items.yaml`,",
        "`src/Qyl.AutoInstrumentation.SourceGenerators/InstrumentationContract.cs`, and",
        "`src/Qyl.AutoInstrumentation.SourceGenerators/QylAutoInstrumentationGenerator.cs`.",
        "",
        "It is the review artifact for the 60-item auto-instrumentation contract: upstream",
        "contract item on the left, qyl NativeAOT interceptor/runtime status on the right.",
        "",
        "## Counts",
        "",
        "| Count | Value |",
        "|---|---:|",
        f"| Total contract items | {counts['total']} |",
        f"| Source-generated signal bindings | {counts['source_generated_signals']} |",
        f"| Unsupported NativeAOT parity/dynamic signals | {counts['unsupported_signals']} |",
        f"| Runtime environment controls | {counts['environment_controls']} |",
        f"| Runtime instrumentation options | {counts['instrumentation_options']} |",
        f"| Missing bindings | {counts['missing']} |",
        "",
        "## Status legend",
        "",
        "| Status | Meaning |",
        "|---|---|",
        "| `source_generated_signal` | The source generator has a source-visible call-site binding for this signal. |",
        "| `unsupported_nativeaot_parity_or_dynamic_signal` | The upstream contract item is retained for parity, but it is not reachable as a NativeAOT source-interceptor signal. |",
        "| `runtime_environment_control` | The runtime options model binds the global/signal environment control. |",
        "| `runtime_instrumentation_option` | The runtime options model binds the instrumentation option. |",
        "| `missing_*` | Fails the gate. |",
        "",
        "## Matrix",
        "",
        "| # | Contract item | Kind | Key | qyl status | Evidence |",
        "|---:|---|---|---|---|---|",
    ]

    for record in report["items"]:
        evidence = "<br>".join(record["evidence"]) if record["evidence"] else "-"
        lines.append(
            "| "
            + str(record["index"])
            + " | `"
            + record["contract_item_id"]
            + "` | `"
            + record["kind"]
            + "` | `"
            + record["key"]
            + "` | `"
            + record["status"]
            + "` | "
            + evidence
            + " |"
        )

    lines.extend(
        [
            "",
            "// validated 2026-06-05 by tools/verify-contract-coverage-report.py",
            "",
        ]
    )
    return "\n".join(lines)


def verify_markdown(report: dict[str, Any]) -> None:
    expected = render_markdown(report)
    if not COVERAGE_MATRIX_PATH.exists():
        fail(f"coverage matrix missing: {COVERAGE_MATRIX_PATH}")

    actual = COVERAGE_MATRIX_PATH.read_text(encoding="utf-8")
    if actual != expected:
        received = COVERAGE_MATRIX_PATH.with_suffix(".received.md")
        received.write_text(expected, encoding="utf-8")
        fail(
            "coverage matrix is stale\n"
            f"expected={COVERAGE_MATRIX_PATH}\n"
            f"received={received}"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify and optionally emit the 60-item qyl contract coverage report.")
    parser.add_argument("--json", type=Path, help="Write the full machine-readable report to this path.")
    parser.add_argument("--markdown", type=Path, help="Write the generated coverage matrix markdown to this path.")
    args = parser.parse_args()

    report = build_report(load_verifier())
    if args.json is not None:
        args.json.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n", encoding="utf-8")
    if args.markdown is not None:
        args.markdown.write_text(render_markdown(report), encoding="utf-8")
    else:
        verify_markdown(report)

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
