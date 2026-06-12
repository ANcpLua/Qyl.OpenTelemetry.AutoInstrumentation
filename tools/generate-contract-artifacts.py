#!/usr/bin/env python3
from __future__ import annotations

import argparse
import difflib
import json
import re
import sys
from pathlib import Path
from typing import Any

try:
    import yaml
except ModuleNotFoundError as exc:  # pragma: no cover - dependency guard for clean machines.
    raise SystemExit("PyYAML is required; run: python3 -m pip install -r tools/requirements.txt") from exc

ROOT = Path(__file__).resolve().parents[1]
UPSTREAM_CONTRACT_PATH = ROOT / "docs" / "contracts" / "otel-dotnet-auto-60.upstream.yaml"
OWNERSHIP_PATH = ROOT / "docs" / "contracts" / "qyl-aot-ownership.yaml"
RESOLVED_CONTRACT_PATH = ROOT / "docs" / "generated" / "qyl-aot-contract.resolved.yaml"
SCHEMA_PATH = ROOT / "docs" / "generated" / "qyl-aot-contract.schema.json"
COVERAGE_MATRIX_PATH = ROOT / "docs" / "coverage-matrix.md"
CONFORMANCE_PLAN_PATH = ROOT / "docs" / "qyl-aot-autoinstrumentation.conformance-plan.json"
README_PATH = ROOT / "README.md"
CONTRACT_CS_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "InstrumentationContract.cs"

CONFORMANCE_PROFILES = [
    {
        "service_name": "qyl-webapi-aot-demo",
        "profile_id": "qyl-aot-webapi",
        "signal_names": [
            "aspnetcore.server",
            "httpclient.downstream",
            "httpclient.self",
        ],
    },
    {
        "service_name": "qyl-db-aot-demo",
        "profile_id": "qyl-aot-db",
        "signal_names": [
            "adonet.command",
            "efcore.sqlite",
            "mongodb.command",
            "mysqlconnector.command",
            "mysqldata.command",
            "npgsql.command",
            "oraclemda.command",
            "sqlite.command",
            "sqlclient.command",
        ],
    },
    {
        "service_name": "qyl-grpc-aot-demo",
        "profile_id": "qyl-aot-grpc",
        "signal_names": [
            "grpc.client",
        ],
    },
    {
        "service_name": "qyl-azure-aot-demo",
        "profile_id": "qyl-aot-azure",
        "signal_names": [
            "azure.sdk",
        ],
    },
    {
        "service_name": "qyl-search-aot-demo",
        "profile_id": "qyl-aot-search",
        "signal_names": [
            "elasticsearch.request",
            "elastictransport.request",
        ],
    },
    {
        "service_name": "qyl-graphql-aot-demo",
        "profile_id": "qyl-aot-graphql",
        "signal_names": [
            "graphql.execute",
        ],
    },
    {
        "service_name": "qyl-messaging-aot-demo",
        "profile_id": "qyl-aot-messaging",
        "signal_names": [
            "kafka.message",
            "masstransit.message",
            "rabbitmq.publish",
        ],
    },
    {
        "service_name": "qyl-cache-aot-demo",
        "profile_id": "qyl-aot-cache",
        "signal_names": [
            "redis.command",
        ],
    },
    {
        "service_name": "qyl-scheduler-aot-demo",
        "profile_id": "qyl-aot-scheduler",
        "signal_names": [
            "quartz.execute",
        ],
    },
    {
        "service_name": "qyl-logging-aot-demo",
        "profile_id": "qyl-aot-logging",
        "signal_names": [
            "ilogger.log",
            "log4net.log",
            "nlog.log",
        ],
    },
    {
        "service_name": "qyl-metrics-aot-demo",
        "profile_id": "qyl-aot-metrics",
        "signal_names": [
            "aspnetcore.components.render_diff.duration",
            "aspnetcore.components.render_diff.size",
            "aspnetcore.components.update_parameters.duration",
            "http.client.request.duration",
            "db.client.operation.duration",
            "dotnet.gc.collections",
            "dotnet.gc.last_collection.heap.size",
            "dotnet.thread_pool.thread.count",
            "dotnet.process.cpu.time",
            "dotnet.process.memory.working_set",
            "dotnet.process.cpu.count",
        ],
    },
    {
        "service_name": "qyl-unsupported-nativeaot-demo",
        "profile_id": "qyl-aot-unsupported-nativeaot",
        "signal_names": [],
    },
]
REQUIRED_CONFORMANCE_PROFILE_IDS = {
    "qyl-aot-azure",
    "qyl-aot-cache",
    "qyl-aot-db",
    "qyl-aot-graphql",
    "qyl-aot-grpc",
    "qyl-aot-logging",
    "qyl-aot-messaging",
    "qyl-aot-metrics",
    "qyl-aot-scheduler",
    "qyl-aot-search",
    "qyl-aot-unsupported-nativeaot",
    "qyl-aot-webapi",
}
README_START = "<!-- qyl-contract-summary:start -->"
README_END = "<!-- qyl-contract-summary:end -->"

SIGNAL_KIND = "signal_specific_instrumentation_promise"
CONTROL_KIND = "global_environment_control"
OPTION_KIND = "instrumentation_option"

LANES = {
    "source_interceptor",
    "runtime_public_telemetry",
    "framework_initialization",
    "official_library_hook",
    "environment_control",
    "instrumentation_option",
    "unsupported_nativeaot",
}
STATUSES = {
    "implemented",
    "control_bound",
    "option_bound",
    "research_required",
    "unsupported_nativeaot",
}
CALL_SITE_VISIBILITIES = {"user_code", "library_internal", "both", "not_applicable"}
PAYLOAD_ACCESS_VALUES = {"typed_public", "reflection_required", "not_applicable"}
EVIDENCE_LEVELS = {"none", "verified_nativeaot", "verified_managed", "compile_binding_only", "option_bound"}
CONFORMANCE_KINDS = {"span", "metric", "log"}
MANAGED_NATIVEAOT_BOUNDARY_SIGNAL_KEYS = {
    "signals.traces.NSERVICEBUS",
    "signals.traces.WCFCLIENT",
    "signals.metrics.NSERVICEBUS",
}
IMPLEMENTED_COMPILE_BINDING_ONLY_ALLOWLIST: set[str] = set()

COMMON_ITEM_PROPERTIES = {
    "kind",
    "key",
    "status",
    "index",
    "contract_item_id",
    "lane",
    "qyl_status",
    "call_site_visibility",
    "payload_access",
    "evidence_level",
    "primary_owner",
    "evidence",
    "conformance_signals",
}
SIGNAL_ITEM_PROPERTIES = COMMON_ITEM_PROPERTIES | {
    "signal",
    "instrumentation_id",
    "environment_toggle",
    "environment_toggle_origin",
    "libraries",
    "supported_versions",
    "instrumentation_types",
    "instrumentation_type_operator",
    "not_supported_on",
    "notes",
    "documentation",
}
CONTROL_ITEM_PROPERTIES = COMMON_ITEM_PROPERTIES | {
    "environment_variable",
    "scope",
    "description",
    "default_value",
    "signal",
    "overrides",
    "is_pattern",
    "placeholder",
}
OPTION_ITEM_PROPERTIES = COMMON_ITEM_PROPERTIES | {
    "environment_variable",
    "target_signal",
    "target_instrumentation_id",
    "description",
    "attribute_names",
    "default_value",
    "default_behavior",
    "value_format",
    "value_type",
    "not_supported_on",
    "notes",
}
ALLOWED_ITEM_PROPERTIES_BY_KIND = {
    SIGNAL_KIND: SIGNAL_ITEM_PROPERTIES,
    CONTROL_KIND: CONTROL_ITEM_PROPERTIES,
    OPTION_KIND: OPTION_ITEM_PROPERTIES,
}


class ContractError(RuntimeError):
    pass


def fail(message: str) -> None:
    raise ContractError(message)


def load_yaml_file(path: Path) -> dict[str, Any]:
    if not path.exists():
        fail(f"missing contract source: {path.relative_to(ROOT)}")
    data = yaml.safe_load(path.read_text(encoding="utf-8"))
    if not isinstance(data, dict):
        fail(f"YAML root must be an object: {path.relative_to(ROOT)}")
    return data


def load_contract() -> dict[str, Any]:
    upstream = load_yaml_file(UPSTREAM_CONTRACT_PATH)
    ownership = load_yaml_file(OWNERSHIP_PATH)
    return resolve_contract(upstream, ownership)


def resolve_contract(upstream: dict[str, Any], ownership: dict[str, Any]) -> dict[str, Any]:
    upstream_items = upstream.get("contract_items")
    ownership_items = ownership.get("ownership_items")
    if not isinstance(upstream_items, list):
        fail("upstream contract must contain contract_items[]")
    if not isinstance(ownership_items, list):
        fail("qyl ownership overlay must contain ownership_items[]")

    overlay_by_id: dict[str, dict[str, Any]] = {}
    for raw_overlay in ownership_items:
        if not isinstance(raw_overlay, dict):
            fail(f"ownership item must be an object: {raw_overlay!r}")
        contract_item_id = str(raw_overlay.get("contract_item_id", ""))
        if not contract_item_id:
            fail("ownership item missing contract_item_id")
        if contract_item_id in overlay_by_id:
            fail(f"duplicate ownership item: {contract_item_id}")
        overlay_by_id[contract_item_id] = dict(raw_overlay)

    resolved_items: list[dict[str, Any]] = []
    for raw_item in sorted(upstream_items, key=lambda candidate: int(candidate["index"])):
        if not isinstance(raw_item, dict):
            fail(f"upstream contract item must be an object: {raw_item!r}")
        item = dict(raw_item)
        contract_item_id = str(item.get("contract_item_id", ""))
        overlay = overlay_by_id.pop(contract_item_id, None)
        if overlay is None:
            fail(f"qyl ownership overlay missing contract item: {contract_item_id}")
        if str(overlay.get("key", "")) != str(item.get("key", "")):
            fail(f"qyl ownership overlay key mismatch for {contract_item_id}")
        for field in [
            "lane",
            "qyl_status",
            "call_site_visibility",
            "payload_access",
            "evidence_level",
            "primary_owner",
            "evidence",
            "conformance_signals",
        ]:
            if field in overlay:
                item[field] = overlay[field]
        resolved_items.append(item)

    if overlay_by_id:
        fail(f"qyl ownership overlay contains unknown contract items: {sorted(overlay_by_id)}")

    return {
        "schema_id": "qyl-aot-autoinstrumentation-resolved-contract",
        "schema_version": str(upstream.get("schema_version", "1.0.0")),
        "generated_at": str(upstream.get("generated_at", "2026-06-04")),
        "source": upstream.get("source", {}),
        "generated_from": {
            "upstream_contract": str(UPSTREAM_CONTRACT_PATH.relative_to(ROOT)),
            "qyl_ownership": str(OWNERSHIP_PATH.relative_to(ROOT)),
        },
        "contract_items": resolved_items,
    }


def contract_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return list(contract["contract_items"])


def signal_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return [item for item in contract_items(contract) if item["kind"] == SIGNAL_KIND]


def implemented_signal_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return [item for item in signal_items(contract) if item.get("qyl_status") == "implemented"]


def source_interceptor_signal_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        item
        for item in implemented_signal_items(contract)
        if item.get("lane") == "source_interceptor"
    ]


def runtime_public_telemetry_signal_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return [
        item
        for item in implemented_signal_items(contract)
        if item.get("lane") == "runtime_public_telemetry"
    ]


def unsupported_signal_items(contract: dict[str, Any]) -> list[dict[str, Any]]:
    return [item for item in signal_items(contract) if item.get("qyl_status") == "unsupported_nativeaot"]


def contract_counts(contract: dict[str, Any]) -> dict[str, int]:
    items = contract_items(contract)
    signals = signal_items(contract)
    return {
        "signal_specific_instrumentation_promises": len(signals),
        "global_environment_controls": sum(1 for item in items if item["kind"] == CONTROL_KIND),
        "instrumentation_options": sum(1 for item in items if item["kind"] == OPTION_KIND),
        "total_contract_items": len(items),
        "traces_signal_specific_promises": sum(1 for item in signals if item.get("signal") == "traces"),
        "metrics_signal_specific_promises": sum(1 for item in signals if item.get("signal") == "metrics"),
        "logs_signal_specific_promises": sum(1 for item in signals if item.get("signal") == "logs"),
        "unique_instrumentation_ids": len({str(item.get("instrumentation_id")) for item in signals}),
    }


def verify_contract_model(contract: dict[str, Any]) -> None:
    items = contract_items(contract)
    if len(items) != 60:
        fail(f"wrong contract item count: {len(items)}")

    indexes = [int(item["index"]) for item in items]
    if indexes != list(range(1, 61)):
        fail(f"contract indexes must be contiguous 1..60: {indexes}")

    ids = [str(item["contract_item_id"]) for item in items]
    expected_ids = [f"OTEL_DOTNET_AUTO_CONTRACT_{index:03d}" for index in range(1, 61)]
    if ids != expected_ids:
        fail("contract_item_id sequence mismatch")

    counts = contract_counts(contract)
    expected_counts = {
        "signal_specific_instrumentation_promises": 37,
        "global_environment_controls": 7,
        "instrumentation_options": 16,
        "total_contract_items": 60,
        "traces_signal_specific_promises": 26,
        "metrics_signal_specific_promises": 8,
        "logs_signal_specific_promises": 3,
        "unique_instrumentation_ids": 31,
    }
    if counts != expected_counts:
        fail(f"contract count mismatch: expected={expected_counts} actual={counts}")

    seen_keys: set[str] = set()
    for item in items:
        verify_contract_item(item)
        key = str(item["key"])
        if key in seen_keys:
            fail(f"duplicate contract key: {key}")
        seen_keys.add(key)

    verify_managed_nativeaot_boundary_semantics(contract)
    verify_conformance_signal_semantics(contract)


def verify_contract_item(item: dict[str, Any]) -> None:
    kind = str(item.get("kind", ""))
    key = str(item.get("key", ""))
    if kind not in ALLOWED_ITEM_PROPERTIES_BY_KIND:
        fail(f"unknown contract item kind for {key}: {kind}")
    unexpected = sorted(set(item) - ALLOWED_ITEM_PROPERTIES_BY_KIND[kind])
    if unexpected:
        fail(f"unexpected properties for {key}: {unexpected}")

    for field in [
        "lane",
        "qyl_status",
        "call_site_visibility",
        "payload_access",
        "evidence_level",
        "primary_owner",
        "evidence",
    ]:
        if field not in item:
            fail(f"missing qyl ownership field {field}: {key}")

    lane = str(item.get("lane", ""))
    status = str(item.get("qyl_status", ""))
    visibility = str(item.get("call_site_visibility", ""))
    payload = str(item.get("payload_access", ""))
    evidence_level = str(item.get("evidence_level", ""))
    evidence = item.get("evidence")

    if lane not in LANES:
        fail(f"invalid lane for {key}: {lane}")
    if status not in STATUSES:
        fail(f"invalid qyl_status for {key}: {status}")
    if visibility not in CALL_SITE_VISIBILITIES:
        fail(f"invalid call_site_visibility for {key}: {visibility}")
    if payload not in PAYLOAD_ACCESS_VALUES:
        fail(f"invalid payload_access for {key}: {payload}")
    if evidence_level not in EVIDENCE_LEVELS:
        fail(f"invalid evidence_level for {key}: {evidence_level}")
    if not isinstance(evidence, list) or any(not isinstance(entry, str) for entry in evidence):
        fail(f"evidence must be a string array for {key}")

    if visibility == "library_internal" and lane == "source_interceptor":
        fail(f"library_internal item cannot use source_interceptor lane: {key}")
    if lane == "runtime_public_telemetry" and payload != "typed_public":
        fail(f"runtime_public_telemetry item must use typed_public payload access: {key}")
    if payload == "reflection_required" and status not in {"research_required", "unsupported_nativeaot"}:
        fail(f"reflection_required item must be research_required or unsupported_nativeaot: {key}")
    if key == "signals.traces.SQLCLIENT" and lane != "source_interceptor":
        fail("SqlClient trace contract must remain interceptor-primary")

    if status == "implemented":
        if evidence_level == "none":
            fail(f"implemented item must carry evidence_level != none: {key}")
        if not evidence:
            fail(f"implemented item must carry evidence: {key}")
        missing_evidence_paths = [entry for entry in evidence if not (ROOT / entry).exists()]
        if missing_evidence_paths:
            fail(f"implemented item evidence path does not exist for {key}: {missing_evidence_paths}")
        if lane == "source_interceptor":
            required_tokens = [
                "src/Qyl.AutoInstrumentation.SourceGenerators/QylAutoInstrumentationGenerator.cs",
                "tools/verify-source-interceptor-consumer.py",
            ]
            for token in required_tokens:
                if token not in evidence:
                    fail(f"source_interceptor item must carry generator and consumer proof for {key}: missing {token}")
            if evidence_level != "compile_binding_only" and not any(
                entry.startswith("tools/verify-real-") and entry.endswith("-demo.py")
                for entry in evidence
            ):
                fail(f"source_interceptor item must carry real demo verifier evidence: {key}")
        if evidence_level == "compile_binding_only":
            if key not in IMPLEMENTED_COMPILE_BINDING_ONLY_ALLOWLIST:
                fail(
                    "implemented compile_binding_only signals require an explicit allowlist entry: "
                    f"{key}"
                )
            runtime_evidence = [
                entry
                for entry in evidence
                if (
                    entry.startswith("tools/verify-real-")
                    or entry
                    in {
                        "tools/smoketest.sh",
                        "tools/verify-nativeaot-consumer.py",
                        "tools/verify-webapi-aot-demo.py",
                    }
                )
            ]
            if runtime_evidence:
                fail(f"compile_binding_only item must not claim runtime verification evidence for {key}: {runtime_evidence}")
        if lane == "runtime_public_telemetry":
            proof_tokens = [
                "src/Qyl.AutoInstrumentation.DiagnosticListeners",
                "src/Qyl.AutoInstrumentation/QylMetricMeters.cs",
                "ILogger",
                "ActivitySource",
                "Meter",
            ]
            if not any(token in entry or token == entry for entry in evidence for token in proof_tokens):
                fail(f"runtime_public_telemetry item must carry typed public payload or meter/activity/log proof: {key}")
    if status == "control_bound" and lane != "environment_control":
        fail(f"control_bound item must use environment_control lane: {key}")
    if status == "option_bound" and lane != "instrumentation_option":
        fail(f"option_bound item must use instrumentation_option lane: {key}")


def verify_managed_nativeaot_boundary_semantics(contract: dict[str, Any]) -> None:
    managed_keys = {
        str(item["key"])
        for item in signal_items(contract)
        if item.get("qyl_status") == "implemented"
        and item.get("evidence_level") == "verified_managed"
    }
    if managed_keys != MANAGED_NATIVEAOT_BOUNDARY_SIGNAL_KEYS:
        fail(
            "verified_managed implemented signals must be an explicit NativeAOT boundary set: "
            f"missing_boundary={sorted(managed_keys - MANAGED_NATIVEAOT_BOUNDARY_SIGNAL_KEYS)} "
            f"stale_boundary={sorted(MANAGED_NATIVEAOT_BOUNDARY_SIGNAL_KEYS - managed_keys)}"
        )


def verify_conformance_signal_semantics(contract: dict[str, Any]) -> None:
    signals_by_name: dict[str, tuple[str, tuple[str, ...], tuple[str, ...], tuple[str, ...]]] = {}
    for item in signal_items(contract):
        conformance_signals = item.get("conformance_signals", [])
        if conformance_signals is None:
            conformance_signals = []
        if not isinstance(conformance_signals, list):
            fail(f"conformance_signals must be an array: {item['key']}")
        if (
            item.get("qyl_status") == "implemented"
            and item.get("evidence_level") == "verified_nativeaot"
            and not conformance_signals
        ):
            fail(f"verified NativeAOT signal must declare conformance_signals: {item['key']}")
        if conformance_signals and item.get("qyl_status") != "implemented":
            fail(f"conformance signal declared for non-implemented item: {item['key']}")
        if conformance_signals and item.get("evidence_level") != "verified_nativeaot":
            fail(f"conformance signal declared for non-NativeAOT evidence item: {item['key']}")
        for signal in conformance_signals:
            if not isinstance(signal, dict):
                fail(f"conformance signal must be an object: {item['key']}")
            if set(signal) != {"kind", "name", "required_attributes", "recommended_attributes", "opt_in_attributes"}:
                fail(f"conformance signal has unexpected shape: {item['key']} {signal}")
            kind = str(signal["kind"])
            name = str(signal["name"])
            if kind not in CONFORMANCE_KINDS:
                fail(f"invalid conformance signal kind for {name}: {kind}")
            for attr_field in ["required_attributes", "recommended_attributes", "opt_in_attributes"]:
                attrs = signal[attr_field]
                if not isinstance(attrs, list) or any(not isinstance(attr, str) for attr in attrs):
                    fail(f"{attr_field} must be a string array for conformance signal {name}")
                if len(attrs) != len(set(attrs)):
                    fail(f"duplicate {attr_field} entries for conformance signal {name}")
            if "error.type" in signal["required_attributes"]:
                fail(f"error.type must not be required in conformance signal {name}")
            signature = (
                kind,
                tuple(str(attr) for attr in signal["required_attributes"]),
                tuple(str(attr) for attr in signal["recommended_attributes"]),
                tuple(str(attr) for attr in signal["opt_in_attributes"]),
            )
            previous_signature = signals_by_name.get(name)
            if previous_signature is not None and previous_signature != signature:
                fail(f"conformance signal name has conflicting declarations: {name}")
            signals_by_name[name] = signature

    assigned_names: set[str] = set()
    profile_ids: set[str] = set()
    service_names: set[str] = set()
    for profile in CONFORMANCE_PROFILES:
        profile_id = str(profile["profile_id"])
        service_name = str(profile["service_name"])
        if profile_id in profile_ids:
            fail(f"duplicate conformance profile_id: {profile_id}")
        if service_name in service_names:
            fail(f"duplicate conformance service_name: {service_name}")
        profile_ids.add(profile_id)
        service_names.add(service_name)
        if profile_id == "qyl-aot-unsupported-nativeaot" and profile["signal_names"]:
            fail("unsupported NativeAOT conformance profile must expect no signals")
        if profile_id != "qyl-aot-unsupported-nativeaot" and not profile["signal_names"]:
            fail(f"conformance profile must expect at least one signal: {profile_id}")
        for name in profile["signal_names"]:
            if name in assigned_names:
                fail(f"conformance signal assigned to multiple profiles: {name}")
            assigned_names.add(name)

    if profile_ids != REQUIRED_CONFORMANCE_PROFILE_IDS:
        fail(
            "conformance plan must preserve multi-profile fixture coverage: "
            f"missing={sorted(REQUIRED_CONFORMANCE_PROFILE_IDS - profile_ids)} "
            f"stale={sorted(profile_ids - REQUIRED_CONFORMANCE_PROFILE_IDS)}"
        )

    names = set(signals_by_name)
    missing_profile = names - assigned_names
    stale_profile = assigned_names - names
    if missing_profile or stale_profile:
        fail(
            "conformance signal/profile mismatch: "
            f"missing_profile={sorted(missing_profile)} stale_profile={sorted(stale_profile)}"
        )

    sql = next((signal for signal in conformance_signals_for_plan(contract) if signal["name"] == "sqlclient.command"), None)
    if sql is None:
        fail("sqlclient.command conformance signal missing")
    if set(sql["required_attributes"]) != {"db.system.name", "db.operation.name", "db.query.summary", "qyl.instrumentation.domain"}:
        fail("sqlclient.command required attributes must come from semantic ownership, not error snapshots")
    if set(sql["recommended_attributes"]) != {"error.type"}:
        fail("sqlclient.command error.type must be recommended")
    if set(sql["opt_in_attributes"]) != {"db.query.text"}:
        fail("sqlclient.command db.query.text must be opt-in")


def conformance_signals_for_plan(contract: dict[str, Any]) -> list[dict[str, Any]]:
    signals: list[dict[str, Any]] = []
    for item in signal_items(contract):
        for signal in item.get("conformance_signals", []) or []:
            signals.append(signal)
    return sorted(signals, key=lambda candidate: str(candidate["name"]))


def expected_outputs(contract: dict[str, Any]) -> dict[Path, str]:
    return {
        RESOLVED_CONTRACT_PATH: render_resolved_yaml(contract),
        SCHEMA_PATH: render_schema(),
        CONTRACT_CS_PATH: render_contract_cs(contract),
        COVERAGE_MATRIX_PATH: render_coverage_matrix(contract),
        CONFORMANCE_PLAN_PATH: render_conformance_plan(contract),
        README_PATH: render_readme(contract),
    }


def render_resolved_yaml(contract: dict[str, Any]) -> str:
    text = yaml.safe_dump(contract, sort_keys=False, allow_unicode=True, width=120)
    if not text.endswith("\n"):
        text += "\n"
    return "# <auto-generated/>\n# Regenerate with tools/generate-contract-artifacts.py --write.\n" + text


def render_schema() -> str:
    common_required = [
        "kind",
        "key",
        "status",
        "index",
        "contract_item_id",
        "lane",
        "qyl_status",
        "call_site_visibility",
        "payload_access",
        "evidence_level",
        "primary_owner",
        "evidence",
    ]
    schema = {
        "$schema": "https://json-schema.org/draft/2020-12/schema",
        "$id": "https://qyl.dev/schemas/qyl-aot-contract.schema.json",
        "$comment": "<auto-generated/> Regenerate with tools/generate-contract-artifacts.py --write.",
        "type": "object",
        "additionalProperties": False,
        "required": ["schema_id", "schema_version", "generated_at", "source", "generated_from", "contract_items"],
        "properties": {
            "schema_id": {"const": "qyl-aot-autoinstrumentation-resolved-contract"},
            "schema_version": {"type": "string"},
            "generated_at": {"type": "string"},
            "source": {"type": "object"},
            "generated_from": {
                "type": "object",
                "additionalProperties": False,
                "required": ["upstream_contract", "qyl_ownership"],
                "properties": {
                    "upstream_contract": {"const": "docs/contracts/otel-dotnet-auto-60.upstream.yaml"},
                    "qyl_ownership": {"const": "docs/contracts/qyl-aot-ownership.yaml"},
                },
            },
            "contract_items": {
                "type": "array",
                "minItems": 60,
                "maxItems": 60,
                "items": {"$ref": "#/$defs/contract_item"},
            },
        },
        "$defs": {
            "contract_item": {
                "oneOf": [
                    {"$ref": "#/$defs/signal_item"},
                    {"$ref": "#/$defs/control_item"},
                    {"$ref": "#/$defs/option_item"},
                ],
            },
            "common_properties": common_schema_properties(),
            "signal_item": item_schema(SIGNAL_KIND, common_required + ["signal", "instrumentation_id", "environment_toggle", "libraries", "supported_versions", "instrumentation_types", "instrumentation_type_operator"], SIGNAL_ITEM_PROPERTIES),
            "control_item": item_schema(CONTROL_KIND, common_required + ["environment_variable", "scope", "description"], CONTROL_ITEM_PROPERTIES),
            "option_item": item_schema(OPTION_KIND, common_required + ["environment_variable", "target_signal", "target_instrumentation_id", "description", "attribute_names"], OPTION_ITEM_PROPERTIES),
            "conformance_signal": {
                "type": "object",
                "additionalProperties": False,
                "required": ["kind", "name", "required_attributes", "recommended_attributes", "opt_in_attributes"],
                "properties": {
                    "kind": {"enum": sorted(CONFORMANCE_KINDS)},
                    "name": {"type": "string"},
                    "required_attributes": string_array_schema(),
                    "recommended_attributes": string_array_schema(),
                    "opt_in_attributes": string_array_schema(),
                },
            },
        },
        "allOf": [
            {
                "if": {"properties": {"contract_items": {"contains": {"properties": {"lane": {"const": "runtime_public_telemetry"}}}}}},
                "then": {"properties": {"contract_items": {"items": {"if": {"properties": {"lane": {"const": "runtime_public_telemetry"}}}, "then": {"properties": {"payload_access": {"const": "typed_public"}}}}}}},
            }
        ],
    }
    return json.dumps(schema, indent=2, sort_keys=False) + "\n"


def common_schema_properties() -> dict[str, Any]:
    return {
        "kind": {"enum": [SIGNAL_KIND, CONTROL_KIND, OPTION_KIND]},
        "key": {"type": "string"},
        "status": {"type": "string"},
        "index": {"type": "integer", "minimum": 1, "maximum": 60},
        "contract_item_id": {"type": "string", "pattern": "^OTEL_DOTNET_AUTO_CONTRACT_[0-9]{3}$"},
        "lane": {"enum": sorted(LANES)},
        "qyl_status": {"enum": sorted(STATUSES)},
        "call_site_visibility": {"enum": sorted(CALL_SITE_VISIBILITIES)},
        "payload_access": {"enum": sorted(PAYLOAD_ACCESS_VALUES)},
        "evidence_level": {"enum": sorted(EVIDENCE_LEVELS)},
        "primary_owner": {"type": "string"},
        "evidence": string_array_schema(),
        "conformance_signals": {"type": "array", "items": {"$ref": "#/$defs/conformance_signal"}},
    }


def item_schema(kind: str, required: list[str], allowed_properties: set[str]) -> dict[str, Any]:
    properties = common_schema_properties()
    properties.update(
        {
            "kind": {"const": kind},
            "signal": {"enum": ["traces", "metrics", "logs"]},
            "instrumentation_id": {"type": "string"},
            "environment_toggle": {"type": "string"},
            "environment_toggle_origin": {"type": "string"},
            "libraries": {"type": "array"},
            "supported_versions": {"type": "string"},
            "instrumentation_types": string_array_schema(),
            "instrumentation_type_operator": {"type": "string"},
            "not_supported_on": string_array_schema(),
            "notes": string_array_schema(),
            "documentation": {"type": "object"},
            "environment_variable": {"type": "string"},
            "scope": {"type": "string"},
            "description": {"type": "string"},
            "default_value": {},
            "overrides": string_array_schema(),
            "is_pattern": {"type": "boolean"},
            "placeholder": {"type": "string"},
            "target_signal": {"enum": ["traces", "metrics", "logs"]},
            "target_instrumentation_id": {"type": "string"},
            "attribute_names": string_array_schema(),
            "default_behavior": {"type": "string"},
            "value_format": {"type": "string"},
            "value_type": {"type": "string"},
        }
    )
    return {
        "type": "object",
        "additionalProperties": False,
        "required": required,
        "properties": {key: properties[key] for key in sorted(allowed_properties)},
        "allOf": [
            {
                "if": {"properties": {"lane": {"const": "runtime_public_telemetry"}}},
                "then": {"properties": {"payload_access": {"const": "typed_public"}}},
            },
            {
                "if": {"properties": {"call_site_visibility": {"const": "library_internal"}}},
                "then": {"not": {"properties": {"lane": {"const": "source_interceptor"}}}},
            },
            {
                "if": {"properties": {"payload_access": {"const": "reflection_required"}}},
                "then": {"not": {"properties": {"qyl_status": {"const": "implemented"}}}},
            },
        ],
    }


def string_array_schema() -> dict[str, Any]:
    return {"type": "array", "items": {"type": "string"}}


def render_contract_cs(contract: dict[str, Any]) -> str:
    counts = contract_counts(contract)
    items = contract_items(contract)
    implemented_keys = [str(item["key"]) for item in implemented_signal_items(contract)]
    source_interceptor_keys = [str(item["key"]) for item in source_interceptor_signal_items(contract)]
    runtime_public_keys = [str(item["key"]) for item in runtime_public_telemetry_signal_items(contract)]
    unsupported_keys = [str(item["key"]) for item in unsupported_signal_items(contract)]
    unique_ids = sorted({str(item.get("instrumentation_id")) for item in signal_items(contract)})

    lines: list[str] = [
        "// <auto-generated/>",
        "// Regenerate with tools/generate-contract-artifacts.py --write.",
        "using System;",
        "using System.Collections.Generic;",
        "using System.Collections.Immutable;",
        "using System.Linq;",
        "using System.Text;",
        "",
        "namespace Qyl.AutoInstrumentation.SourceGenerators;",
        "",
        "internal enum InstrumentationContractKind",
        "{",
        "    SignalSpecificInstrumentationPromise,",
        "    GlobalEnvironmentControl,",
        "    InstrumentationOption,",
        "}",
        "",
        "internal enum InstrumentationSignal",
        "{",
        "    None,",
        "    Traces,",
        "    Metrics,",
        "    Logs,",
        "}",
        "",
        "internal enum InstrumentationContractLane",
        "{",
        "    SourceInterceptor,",
        "    RuntimePublicTelemetry,",
        "    FrameworkInitialization,",
        "    OfficialLibraryHook,",
        "    EnvironmentControl,",
        "    InstrumentationOption,",
        "    UnsupportedNativeAot,",
        "}",
        "",
        "internal enum QylContractStatus",
        "{",
        "    Implemented,",
        "    ControlBound,",
        "    OptionBound,",
        "    ResearchRequired,",
        "    UnsupportedNativeAot,",
        "}",
        "",
        "internal enum SourceVisibility",
        "{",
        "    UserCode,",
        "    LibraryInternal,",
        "    Both,",
        "    NotApplicable,",
        "}",
        "",
        "internal enum PayloadAccess",
        "{",
        "    TypedPublic,",
        "    ReflectionRequired,",
        "    NotApplicable,",
        "}",
        "",
        "internal enum QylEvidenceLevel",
        "{",
        "    None,",
        "    VerifiedNativeAot,",
        "    VerifiedManaged,",
        "    CompileBindingOnly,",
        "    OptionBound,",
        "}",
        "",
        "internal readonly record struct InstrumentationContractItem(",
        "    int Index,",
        "    string ContractItemId,",
        "    InstrumentationContractKind Kind,",
        "    string Key,",
        "    InstrumentationSignal Signal,",
        "    string InstrumentationId,",
        "    string EnvironmentVariable,",
        "    string SupportedVersions,",
        "    ImmutableArray<string> Libraries,",
        "    ImmutableArray<string> InstrumentationTypes,",
        "    string Promise,",
        "    ImmutableArray<string> AttributeNames,",
        "    InstrumentationContractLane Lane,",
        "    QylContractStatus QylStatus,",
        "    SourceVisibility SourceVisibility,",
        "    PayloadAccess PayloadAccess,",
        "    QylEvidenceLevel EvidenceLevel,",
        "    string PrimaryOwner,",
        "    ImmutableArray<string> Evidence);",
        "",
        "internal static class InstrumentationContract",
        "{",
        f"    public const int SignalSpecificInstrumentationPromiseCount = {counts['signal_specific_instrumentation_promises']};",
        f"    public const int GlobalEnvironmentControlCount = {counts['global_environment_controls']};",
        f"    public const int InstrumentationOptionCount = {counts['instrumentation_options']};",
        "    public const int TotalCount =",
        "        SignalSpecificInstrumentationPromiseCount +",
        "        GlobalEnvironmentControlCount +",
        "        InstrumentationOptionCount;",
        "",
        f"    public const int TracesSignalSpecificPromiseCount = {counts['traces_signal_specific_promises']};",
        f"    public const int MetricsSignalSpecificPromiseCount = {counts['metrics_signal_specific_promises']};",
        f"    public const int LogsSignalSpecificPromiseCount = {counts['logs_signal_specific_promises']};",
        f"    public const int UniqueInstrumentationIdCount = {len(unique_ids)};",
        f"    public const int ImplementedSignalPromiseCount = {len(implemented_keys)};",
        f"    public const int SourceInterceptorSignalPromiseCount = {len(source_interceptor_keys)};",
        f"    public const int RuntimePublicTelemetrySignalPromiseCount = {len(runtime_public_keys)};",
        f"    public const int UnsupportedNativeAotSignalPromiseCount = {len(unsupported_keys)};",
        "",
        "    public const string AspNetCoreComponentsMeterName = \"Microsoft.AspNetCore.Components\";",
        "    public const string AspNetCoreComponentsLifecycleMeterName = \"Microsoft.AspNetCore.Components.Lifecycle\";",
        "    public const string AspNetCoreComponentsServerCircuitsMeterName = \"Microsoft.AspNetCore.Components.Server.Circuits\";",
        "    public const string AspNetCoreComponentsNavigateMetricName = \"aspnetcore.components.navigate\";",
        "",
    ]
    lines.extend(render_csharp_string_array("ImplementedSignalKeys", implemented_keys, readonly=True))
    lines.append("")
    lines.extend(render_csharp_string_array("SourceInterceptorSignalKeys", source_interceptor_keys, readonly=True))
    lines.append("")
    lines.extend(render_csharp_string_array("RuntimePublicTelemetrySignalKeys", runtime_public_keys, readonly=True))
    lines.append("")
    lines.extend(render_csharp_string_array("UnsupportedNativeAotSignalKeys", unsupported_keys, readonly=True))
    lines.append("")
    lines.append("    public static readonly ImmutableArray<InstrumentationContractItem> Items =")
    lines.append("        ImmutableArray.Create(")
    for index, item in enumerate(items):
        suffix = "," if index < len(items) - 1 else ""
        lines.append("            " + render_contract_item_ctor(item) + suffix)
    lines.append("        );")
    lines.extend([
        "",
        "    public static string EmitGeneratedManifestSource()",
        "    {",
        "        var builder = new StringBuilder();",
        "        builder.AppendLine(\"// <auto-generated/>\");",
        "        builder.AppendLine(\"namespace Qyl.AutoInstrumentation.Generated;\");",
        "        builder.AppendLine();",
        "        builder.AppendLine(\"internal static class QylGeneratedInstrumentationContract\");",
        "        builder.AppendLine(\"{\");",
        "        builder.AppendLine($\"    public const int SignalSpecificInstrumentationPromiseCount = {SignalSpecificInstrumentationPromiseCount};\");",
        "        builder.AppendLine($\"    public const int GlobalEnvironmentControlCount = {GlobalEnvironmentControlCount};\");",
        "        builder.AppendLine($\"    public const int InstrumentationOptionCount = {InstrumentationOptionCount};\");",
        "        builder.AppendLine($\"    public const int TotalCount = {TotalCount};\");",
        "        builder.AppendLine($\"    public const int TracesSignalSpecificPromiseCount = {TracesSignalSpecificPromiseCount};\");",
        "        builder.AppendLine($\"    public const int MetricsSignalSpecificPromiseCount = {MetricsSignalSpecificPromiseCount};\");",
        "        builder.AppendLine($\"    public const int LogsSignalSpecificPromiseCount = {LogsSignalSpecificPromiseCount};\");",
        "        builder.AppendLine($\"    public const int UniqueInstrumentationIdCount = {UniqueInstrumentationIdCount};\");",
        "        builder.AppendLine($\"    public const int ImplementedSignalPromiseCount = {ImplementedSignalPromiseCount};\");",
        "        builder.AppendLine($\"    public const int SourceInterceptorSignalPromiseCount = {SourceInterceptorSignalPromiseCount};\");",
        "        builder.AppendLine($\"    public const int RuntimePublicTelemetrySignalPromiseCount = {RuntimePublicTelemetrySignalPromiseCount};\");",
        "        builder.AppendLine($\"    public const int UnsupportedNativeAotSignalPromiseCount = {UnsupportedNativeAotSignalPromiseCount};\");",
        "        builder.AppendLine(\"    public const string AspNetCoreComponentsMeterName = \\\"Microsoft.AspNetCore.Components\\\";\");",
        "        builder.AppendLine(\"    public const string AspNetCoreComponentsLifecycleMeterName = \\\"Microsoft.AspNetCore.Components.Lifecycle\\\";\");",
        "        builder.AppendLine(\"    public const string AspNetCoreComponentsServerCircuitsMeterName = \\\"Microsoft.AspNetCore.Components.Server.Circuits\\\";\");",
        "        builder.AppendLine(\"    public const string AspNetCoreComponentsNavigateMetricName = \\\"aspnetcore.components.navigate\\\";\");",
        "        builder.AppendLine();",
        "        EmitStringArray(builder, \"ItemIds\", Items.Select(static item => item.Key));",
        "        EmitStringArray(builder, \"SignalKeys\", Items.Where(static item => item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise).Select(static item => item.Key));",
        "        EmitStringArray(builder, \"ImplementedSignalKeys\", ImplementedSignalKeys);",
        "        EmitStringArray(builder, \"SourceInterceptorSignalKeys\", SourceInterceptorSignalKeys);",
        "        EmitStringArray(builder, \"RuntimePublicTelemetrySignalKeys\", RuntimePublicTelemetrySignalKeys);",
        "        EmitStringArray(builder, \"UnsupportedNativeAotSignalKeys\", UnsupportedNativeAotSignalKeys);",
        "        EmitStringArray(builder, \"GlobalEnvironmentControls\", Items.Where(static item => item.Kind is InstrumentationContractKind.GlobalEnvironmentControl).Select(static item => item.EnvironmentVariable));",
        "        EmitStringArray(builder, \"InstrumentationOptions\", Items.Where(static item => item.Kind is InstrumentationContractKind.InstrumentationOption).Select(static item => item.EnvironmentVariable));",
        "        builder.AppendLine(\"}\");",
        "        return builder.ToString();",
        "    }",
        "",
        "    public static InstrumentationContractItem? TryGetImplementedSignal(string key)",
        "        => TryGetSignal(key, static item => item.QylStatus is QylContractStatus.Implemented);",
        "",
        "    public static InstrumentationContractItem? TryGetSourceInterceptorSignal(string key)",
        "        => TryGetSignal(key, static item => item is { QylStatus: QylContractStatus.Implemented, Lane: InstrumentationContractLane.SourceInterceptor });",
        "",
        "    public static InstrumentationContractItem? TryGetSupportedSignal(string key)",
        "        => TryGetSignal(key, static _ => true);",
        "",
        "    private static InstrumentationContractItem? TryGetSignal(string key, Func<InstrumentationContractItem, bool> predicate)",
        "    {",
        "        foreach (var item in Items)",
        "        {",
        "            if (item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise &&",
        "                predicate(item) &&",
        "                string.Equals(item.Key, key, StringComparison.Ordinal))",
        "            {",
        "                return item;",
        "            }",
        "        }",
        "",
        "        return null;",
        "    }",
        "",
        "    private static void EmitStringArray(StringBuilder builder, string name, IEnumerable<string> values)",
        "    {",
        "        builder.Append(\"    public static string[] \");",
        "        builder.Append(name);",
        "        builder.AppendLine(\" => new[]\");",
        "        builder.AppendLine(\"    {\");",
        "",
        "        foreach (var value in values)",
        "        {",
        "            builder.Append(\"        \\\"\");",
        "            builder.Append(value.Replace(\"\\\\\", \"\\\\\\\\\").Replace(\"\\\"\", \"\\\\\\\"\"));",
        "            builder.AppendLine(\"\\\",\");",
        "        }",
        "",
        "        builder.AppendLine(\"    };\");",
        "        builder.AppendLine();",
        "    }",
        "}",
        "",
    ])
    return "\n".join(lines)


def render_csharp_string_array(name: str, values: list[str], readonly: bool) -> list[str]:
    declaration = "public static readonly ImmutableArray<string>" if readonly else "public static ImmutableArray<string>"
    if not values:
        return [f"    {declaration} {name} = ImmutableArray<string>.Empty;"]
    lines = [f"    {declaration} {name} =", "        ImmutableArray.Create("]
    for index, value in enumerate(values):
        suffix = "," if index < len(values) - 1 else ""
        lines.append(f"            {cs_string(value)}{suffix}")
    lines.append("        );")
    return lines


def render_contract_item_ctor(item: dict[str, Any]) -> str:
    kind = {
        SIGNAL_KIND: "SignalSpecificInstrumentationPromise",
        CONTROL_KIND: "GlobalEnvironmentControl",
        OPTION_KIND: "InstrumentationOption",
    }[str(item["kind"])]
    signal = str(item.get("signal") or item.get("target_signal") or "none")
    signal_name = {"traces": "Traces", "metrics": "Metrics", "logs": "Logs", "none": "None"}.get(signal, "None")
    instrumentation_id = str(item.get("instrumentation_id") or item.get("target_instrumentation_id") or "")
    environment_variable = str(item.get("environment_toggle") or item.get("environment_variable") or "")
    supported_versions = str(item.get("supported_versions") or "")
    libraries = flatten_libraries(item.get("libraries", []))
    instrumentation_types = [str(value) for value in item.get("instrumentation_types", [])]
    attributes = [str(value) for value in item.get("attribute_names", [])]
    evidence = [str(value) for value in item.get("evidence", [])]
    promise = promise_text(item)
    return (
        "new InstrumentationContractItem("
        + ", ".join(
            [
                str(int(item["index"])),
                cs_string(str(item["contract_item_id"])),
                f"InstrumentationContractKind.{kind}",
                cs_string(str(item["key"])),
                f"InstrumentationSignal.{signal_name}",
                cs_string(instrumentation_id),
                cs_string(environment_variable),
                cs_string(supported_versions),
                cs_immutable_array(libraries),
                cs_immutable_array(instrumentation_types),
                cs_string(promise),
                cs_immutable_array(attributes),
                "InstrumentationContractLane." + pascal(str(item["lane"])),
                "QylContractStatus." + pascal(str(item["qyl_status"])),
                "SourceVisibility." + pascal(str(item["call_site_visibility"])),
                "PayloadAccess." + pascal(str(item["payload_access"])),
                "QylEvidenceLevel." + pascal(str(item["evidence_level"])),
                cs_string(str(item["primary_owner"])),
                cs_immutable_array(evidence),
            ]
        )
        + ")"
    )


def flatten_libraries(value: Any) -> list[str]:
    if not isinstance(value, list):
        return []
    names: list[str] = []
    for entry in value:
        if isinstance(entry, str):
            names.append(entry)
        elif isinstance(entry, dict):
            name = entry.get("name")
            if isinstance(name, dict):
                names.append(str(name.get("name", "")))
            elif name is not None:
                names.append(str(name))
    return [name for name in names if name]


def promise_text(item: dict[str, Any]) -> str:
    if item["kind"] == OPTION_KIND or item["kind"] == CONTROL_KIND:
        return str(item.get("description", ""))
    notes = item.get("notes")
    if isinstance(notes, list) and notes:
        return " ".join(str(note) for note in notes)
    documentation = item.get("documentation")
    if isinstance(documentation, dict) and documentation.get("name"):
        return str(documentation["name"])
    return str(item["key"]) + " instrumentation promise."


def pascal(value: str) -> str:
    overrides = {"aot": "Aot", "nativeaot": "NativeAot", "qyl": "Qyl"}
    return "".join(overrides.get(part, part.capitalize()) for part in value.split("_"))


def cs_immutable_array(values: list[str]) -> str:
    if not values:
        return "ImmutableArray<string>.Empty"
    return "ImmutableArray.Create(" + ", ".join(cs_string(value) for value in values) + ")"


def cs_string(value: str) -> str:
    return "\"" + value.replace("\\", "\\\\").replace("\"", "\\\"").replace("\r", "\\r").replace("\n", "\\n") + "\""


def render_coverage_matrix(contract: dict[str, Any]) -> str:
    counts = contract_counts(contract)
    status_counts: dict[str, int] = {}
    lane_counts: dict[str, int] = {}
    evidence_counts: dict[str, int] = {}
    for item in contract_items(contract):
        status_counts[str(item["qyl_status"])] = status_counts.get(str(item["qyl_status"]), 0) + 1
        lane_counts[str(item["lane"])] = lane_counts.get(str(item["lane"]), 0) + 1
        evidence_counts[str(item["evidence_level"])] = evidence_counts.get(str(item["evidence_level"]), 0) + 1

    lines = [
        "# AOT Ownership Coverage Matrix",
        "",
        "<!-- <auto-generated/> -->",
        "<!-- Regenerate with `python3 tools/generate-contract-artifacts.py --write`. -->",
        "",
        "This matrix is generated from `docs/generated/qyl-aot-contract.resolved.yaml`.",
        "The raw upstream contract lives in `docs/contracts/otel-dotnet-auto-60.upstream.yaml`; qyl ownership and evidence live in `docs/contracts/qyl-aot-ownership.yaml`.",
        "",
        "## Counts",
        "",
        "| Count | Value |",
        "|---|---:|",
        f"| Total contract items | {counts['total_contract_items']} |",
        f"| Signal promises | {counts['signal_specific_instrumentation_promises']} |",
        f"| Global environment controls | {counts['global_environment_controls']} |",
        f"| Instrumentation options | {counts['instrumentation_options']} |",
        "",
        "## qyl status counts",
        "",
        "| Status | Count |",
        "|---|---:|",
    ]
    for status in sorted(status_counts):
        lines.append(f"| `{status}` | {status_counts[status]} |")
    lines.extend(["", "## Lane counts", "", "| Lane | Count |", "|---|---:|"])
    for lane in sorted(lane_counts):
        lines.append(f"| `{lane}` | {lane_counts[lane]} |")
    lines.extend(["", "## Evidence counts", "", "| Evidence level | Count |", "|---|---:|"])
    for evidence_level in sorted(evidence_counts):
        lines.append(f"| `{evidence_level}` | {evidence_counts[evidence_level]} |")
    lines.extend(
        [
            "",
            "## Matrix",
            "",
            "| # | Key | Lane | qyl status | Call-site visibility | Payload access | Evidence | Owner |",
            "|---:|---|---|---|---|---|---|---|",
        ]
    )
    for item in contract_items(contract):
        lines.append(
            f"| {int(item['index'])} | `{item['key']}` | `{item['lane']}` | `{item['qyl_status']}` | "
            f"`{item['call_site_visibility']}` | `{item['payload_access']}` | `{item['evidence_level']}` | {escape_md(str(item['primary_owner']))} |"
        )
    lines.append("")
    return "\n".join(lines)


def escape_md(value: str) -> str:
    return value.replace("|", "\\|")


def render_conformance_plan(contract: dict[str, Any]) -> str:
    signals_by_name = {
        str(signal["name"]): signal
        for signal in conformance_signals_for_plan(contract)
    }

    plan = {
        "schema_version": "1",
        "graph_schema_version": "1",
        "services": [],
    }
    for profile in CONFORMANCE_PROFILES:
        expected_signals = []
        for name in profile["signal_names"]:
            signal = signals_by_name[name]
            expected_signals.append(
                {
                    "kind": str(signal["kind"]),
                    "name": str(signal["name"]),
                    "required_attributes": list(signal["required_attributes"]),
                    "recommended_attributes": list(signal["recommended_attributes"]),
                    "opt_in_attributes": list(signal["opt_in_attributes"]),
                }
            )

        plan["services"].append(
            {
                "service_name": str(profile["service_name"]),
                "profile_id": str(profile["profile_id"]),
                "expected_signals": expected_signals,
            }
        )

    return json.dumps(plan, indent=2, sort_keys=False) + "\n"


def render_readme(contract: dict[str, Any]) -> str:
    original = README_PATH.read_text(encoding="utf-8")
    block = render_readme_block(contract)
    if README_START in original and README_END in original:
        return re.sub(
            re.escape(README_START) + r".*?" + re.escape(README_END),
            block.strip(),
            original,
            flags=re.DOTALL,
        )

    anchor = "The conformance processor is off by default:\n\n```bash\nQYL_CONFORMANCE_ENABLED=1\n```\n"
    if anchor not in original:
        fail("README insertion anchor missing")
    return original.replace(anchor, anchor + "\n" + block + "\n", 1)


def render_readme_block(contract: dict[str, Any]) -> str:
    lines = [
        README_START,
        "## Generated contract ownership summary",
        "",
        "<!-- <auto-generated/> -->",
        "<!-- Regenerate with `python3 tools/generate-contract-artifacts.py --write`. -->",
        "",
        "The upstream OpenTelemetry contract, qyl mechanism ownership, and generated resolved contract are split deliberately:",
        "",
        "| Contract layer | Path | Role |",
        "|---|---|---|",
        "| Upstream contract | `docs/contracts/otel-dotnet-auto-60.upstream.yaml` | Raw 60-item OpenTelemetry .NET auto-instrumentation contract. |",
        "| qyl ownership overlay | `docs/contracts/qyl-aot-ownership.yaml` | qyl lane, status, call-site visibility, payload access, evidence, and conformance semantics. |",
        "| Resolved generated contract | `docs/generated/qyl-aot-contract.resolved.yaml` | Joined model used to generate schema, C# contract data, matrix, and conformance plan. |",
        "",
        "| # | Key | Lane | qyl status | Visibility | Payload | Evidence |",
        "|---:|---|---|---|---|---|---|",
    ]
    for item in contract_items(contract):
        lines.append(
            f"| {int(item['index'])} | `{item['key']}` | `{item['lane']}` | `{item['qyl_status']}` | "
            f"`{item['call_site_visibility']}` | `{item['payload_access']}` | `{item['evidence_level']}` |"
        )
    lines.extend(["", README_END, ""])
    return "\n".join(lines)


def verify_generated_files(contract: dict[str, Any]) -> None:
    mismatches: list[str] = []
    for path, expected in expected_outputs(contract).items():
        if not path.exists():
            mismatches.append(f"missing generated artifact: {path.relative_to(ROOT)}")
            continue
        actual = path.read_text(encoding="utf-8")
        if actual != expected:
            diff = "".join(
                difflib.unified_diff(
                    actual.splitlines(keepends=True),
                    expected.splitlines(keepends=True),
                    fromfile=str(path.relative_to(ROOT)),
                    tofile=str(path.relative_to(ROOT)) + " (generated)",
                    n=3,
                )
            )
            mismatches.append(f"stale generated artifact: {path.relative_to(ROOT)}\n{diff[:4000]}")
    if mismatches:
        fail("\n".join(mismatches) + "\nrun: python3 tools/generate-contract-artifacts.py --write")


def write_generated_files(contract: dict[str, Any]) -> None:
    for path, content in expected_outputs(contract).items():
        path.parent.mkdir(parents=True, exist_ok=True)
        path.write_text(content, encoding="utf-8")


def main() -> None:
    parser = argparse.ArgumentParser(description="Generate or verify qyl AOT contract artifacts from upstream + qyl ownership YAML.")
    group = parser.add_mutually_exclusive_group(required=True)
    group.add_argument("--write", action="store_true", help="Regenerate tracked contract artifacts.")
    group.add_argument("--check", action="store_true", help="Fail if tracked contract artifacts are stale.")
    args = parser.parse_args()

    try:
        contract = load_contract()
        verify_contract_model(contract)
        if args.write:
            write_generated_files(contract)
            print("contract-artifacts-generated")
        else:
            verify_generated_files(contract)
            print("contract-artifacts-ok")
    except ContractError as exc:
        raise SystemExit(str(exc)) from exc


if __name__ == "__main__":
    main()
