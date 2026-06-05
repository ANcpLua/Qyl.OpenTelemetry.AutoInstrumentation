#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
RFC = ROOT / "docs" / "rfc" / "0001-interceptor-substrate.md"


REQUIRED_TOKENS = [
    "SemanticModel.GetInterceptableLocation",
    "[InterceptsLocation]",
    "NativeAOT",
    "not a CLR profiler",
    "not runtime IL rewriting",
    "not a startup hook",
    "AssemblyLoadContext",
    "not reflection-based dynamic patching",
    "PackageReference",
    "ProjectReference",
    "build/Qyl.AutoInstrumentation.targets",
    "buildTransitive/Qyl.AutoInstrumentation.targets",
    "Qyl.AutoInstrumentation.SourceGenerators.dll",
    "IL2xxx",
    "IL3xxx",
    "IL4xxx",
    "CA warnings",
    "Public API baseline",
    "source-visible call sites",
    "golden generated-code fixtures",
    "validated 2026-06-05",
]


FORBIDDEN_PHRASES = [
    "CLR profiler substrate",
    "runtime IL rewrite substrate",
    "StartupHook substrate",
    "AssemblyLoadContext substrate",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    if not RFC.exists():
        fail(f"missing RFC artifact: {RFC}")

    text = RFC.read_text(encoding="utf-8")
    lowered = text.lower()
    for token in REQUIRED_TOKENS:
        if token.lower() not in lowered:
            fail(f"RFC missing required token: {token}")

    for phrase in FORBIDDEN_PHRASES:
        if phrase in text:
            fail(f"RFC contains forbidden substrate claim: {phrase}")

    if len(text.split()) < 900:
        fail("RFC is too short to be a reviewable contribution artifact")

    print("rfc-artifact-ok")


if __name__ == "__main__":
    main()
