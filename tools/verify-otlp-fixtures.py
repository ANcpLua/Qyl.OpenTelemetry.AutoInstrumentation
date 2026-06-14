#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
PROPS_PATH = ROOT / "Directory.Build.props"
WEBAPI_REPORT_PATH = ROOT / "tools" / "Qyl.AutoInstrumentation.WebApiAotDemo" / "verified" / "report.json"
OTLP_VERIFIED_PATH = ROOT / "tools" / "Qyl.AutoInstrumentation.OtlpFixtures" / "verified" / "webapi-aot-traces.otlp.json"

EXPECTED_SIGNALS = [
    "aspnetcore.server",
    "efcore.sqlite",
    "httpclient.downstream",
    "httpclient.self",
    "sqlclient.command",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def read_version() -> str:
    text = PROPS_PATH.read_text(encoding="utf-8")
    prefix = "<Version>"
    suffix = "</Version>"
    start = text.find(prefix)
    if start < 0:
        fail("Directory.Build.props is missing <Version>")

    end = text.find(suffix, start)
    if end < 0:
        fail("Directory.Build.props has unterminated <Version>")

    return text[start + len(prefix):end].strip()


def scalar_value(value: str) -> dict[str, Any]:
    if value.isdecimal():
        return {"intValue": int(value)}

    return {"stringValue": value}


def span_kind(kind: str) -> str:
    return {
        "Client": "SPAN_KIND_CLIENT",
        "Internal": "SPAN_KIND_INTERNAL",
        "Producer": "SPAN_KIND_PRODUCER",
        "Consumer": "SPAN_KIND_CONSUMER",
        "Server": "SPAN_KIND_SERVER",
    }.get(kind, "SPAN_KIND_UNSPECIFIED")


def status_code(status: str) -> str:
    return {
        "Error": "STATUS_CODE_ERROR",
        "Ok": "STATUS_CODE_OK",
        "Unset": "STATUS_CODE_UNSET",
    }.get(status, "STATUS_CODE_UNSET")


def span_id(index: int) -> str:
    return f"{index + 1:016x}"


def trace_id(index: int) -> str:
    return f"{index + 1:032x}"


def render_otlp(report: dict[str, Any], version: str) -> dict[str, Any]:
    if report.get("RuntimeMode") != "nativeaot":
        fail("web API verified must be from NativeAOT runtime")

    if report.get("Pass") is not True:
        fail("web API verified report is not passing")

    signals = report.get("Signals")
    if not isinstance(signals, list):
        fail("web API verified report is missing Signals[]")

    actual_signals = sorted(str(signal.get("Signal")) for signal in signals)
    if actual_signals != EXPECTED_SIGNALS:
        fail(f"unexpected signal set: expected={EXPECTED_SIGNALS} actual={actual_signals}")

    spans: list[dict[str, Any]] = []
    for index, signal in enumerate(sorted(signals, key=lambda item: str(item["Signal"]))):
        tags = signal.get("Tags")
        if not isinstance(tags, dict):
            fail(f"signal is missing Tags object: {signal}")

        attributes = [
            {"key": str(key), "value": scalar_value(str(value))}
            for key, value in sorted(tags.items(), key=lambda pair: str(pair[0]))
        ]
        attributes.insert(0, {"key": "qyl.fixture.signal", "value": {"stringValue": str(signal["Signal"])}})

        spans.append(
            {
                "traceId": trace_id(index),
                "spanId": span_id(index),
                "parentSpanId": "",
                "name": str(signal["Name"]),
                "kind": span_kind(str(signal["Kind"])),
                "startTimeUnixNano": "0",
                "endTimeUnixNano": "0",
                "attributes": attributes,
                "status": {"code": status_code(str(signal["Status"]))},
            }
        )

    return {
        "resourceSpans": [
            {
                "resource": {
                    "attributes": [
                        {"key": "service.name", "value": {"stringValue": "qyl-webapi-aot-demo"}},
                        {"key": "telemetry.sdk.language", "value": {"stringValue": "dotnet"}},
                    ]
                },
                "scopeSpans": [
                    {
                        "scope": {
                            "name": "Qyl.AutoInstrumentation",
                            "version": version,
                        },
                        "spans": spans,
                    }
                ],
            }
        ]
    }


def canonical_json(value: dict[str, Any]) -> str:
    return json.dumps(value, indent=2, sort_keys=True) + "\n"


def main() -> None:
    parser = argparse.ArgumentParser(description="Verify canonical OTLP-shaped verified fixtures.")
    parser.add_argument("--update-verified", action="store_true", help="Update committed OTLP-shaped fixtures.")
    args = parser.parse_args()

    if not WEBAPI_REPORT_PATH.exists():
        fail(f"missing web API verified report: {WEBAPI_REPORT_PATH}")

    report = json.loads(WEBAPI_REPORT_PATH.read_text(encoding="utf-8"))
    rendered = canonical_json(render_otlp(report, read_version()))

    if args.update_verified:
        OTLP_VERIFIED_PATH.parent.mkdir(parents=True, exist_ok=True)
        OTLP_VERIFIED_PATH.write_text(rendered, encoding="utf-8")
        print("otlp-fixtures-updated")
        return

    if not OTLP_VERIFIED_PATH.exists():
        fail(f"missing OTLP-shaped verified fixture: {OTLP_VERIFIED_PATH}")

    expected = OTLP_VERIFIED_PATH.read_text(encoding="utf-8")
    if expected != rendered:
        received = OTLP_VERIFIED_PATH.with_suffix(".received.json")
        received.write_text(rendered, encoding="utf-8")
        fail(
            "OTLP-shaped verified fixture mismatch\n"
            f"expected={OTLP_VERIFIED_PATH}\n"
            f"received={received}"
        )

    print("otlp-fixtures-ok")


if __name__ == "__main__":
    main()
