#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
DOC = ROOT / "docs" / "score-100-justification.md"


REQUIRED_TOKENS = [
    "0fce5ab2286fb56c86974bf733f4550e60629049",
    "0.3.0-pre.1",
    "v0.3.0-pre.1",
    "tools/verify-aot-autoinstrumentation-goal.py",
    "contract-coverage-report-ok total=60",
    "aot-warning-gate-ok consumer=package-reference warnings=0",
    "aot-warning-gate-ok consumer=project-reference warnings=0",
    "webapi-aot-demo-ok qyl_warnings=0",
    "otlp-collector-fixtures-ok",
    "DbCommand",
    "EntityFrameworkCore",
    "HttpClient.GetAsync",
    "E: `QylAutoInstrumentationOptions`",
    "D: ProjectReference",
    "d57c3cd",
    "e21ca1d",
    "0fce5ab",
    "docs/rfc/0001-interceptor-substrate.md",
    "Residual risks",
    "validated 2026-06-05 20:19 CEST",
]


FORBIDDEN_TOKENS = [
    "TODO",
    "TBD",
    "0.2.0-pre.1",
    "Final release tag is not created yet",
    "still needed for a complete 100/100 score",
    "Collector-backed OTLP transport fixtures, full runtime XML-doc enforcement",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    if not DOC.exists():
        fail(f"missing score evidence document: {DOC}")

    text = DOC.read_text(encoding="utf-8")
    lowered = text.lower()
    for token in REQUIRED_TOKENS:
        if token.lower() not in lowered:
            fail(f"score evidence document missing token: {token}")

    for token in FORBIDDEN_TOKENS:
        if token.lower() in lowered:
            fail(f"score evidence document contains forbidden stale token: {token}")

    if len(text.split()) < 900:
        fail("score evidence document is too short to defend the score")

    print("score-100-justification-ok")


if __name__ == "__main__":
    main()
