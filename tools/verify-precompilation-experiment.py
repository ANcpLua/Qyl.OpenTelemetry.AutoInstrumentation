#!/usr/bin/env python3
"""Reproduce the pre-compilation contract experiment and assert its measured result.

This is the Phase-8 reproducer for docs/experiments/precompilation-verdict.md. It builds the
ISOLATED experiment under experiment/contract-precompilation/ (which pins the Roslyn nightly
toolset 5.9.0-1.26324.7 carrying RegisterPreCompilationSourceOutput, dotnet/roslyn#83088) and
checks the two-phase contract pipeline produced the expected symbols and compile-time-only
inference. Requires network access to the dnceng dotnet-tools nightly feed; skips (exit 0) if the
toolset cannot be restored, so it never breaks an offline floor run.

NOT wired into verify-aot-autoinstrumentation-goal.py: the experiment lives outside the slnx /
production build graph and depends on an unshipped compiler.
"""
from __future__ import annotations

import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
CONSUMER = ROOT / "experiment" / "contract-precompilation" / "Qyl.Contract.Consumer"

EXPECTED = {
    "pre-comp capability count": (r"ContractRegistry\.CapabilityCount = (\d+)", "37"),
    "standard-phase bound count": (r"BoundCapabilityCount = (\d+)", "37"),
    "ASPNET stays unsupported": (r"signals\.traces\.ASPNET status = (\w+)", "UnsupportedNativeAot"),
    "inferred attribute count": (r"compile-time-only semconv inference \((\d+) attributes", "5"),
}
EXPECTED_BINDINGS = {
    "OrderRequest.CustomerId  ->  customer.id",
    "OrderRequest.OrderId  ->  order.id",
    "OrderRequest.TenantId  ->  tenant.id",
    "ShipmentEvent.CorrelationId  ->  correlation.id",
    "ShipmentEvent.OrderId  ->  order.id",
}


def run(args: list[str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(args, cwd=CONSUMER, capture_output=True, text=True)


def main() -> int:
    if not CONSUMER.exists():
        print(f"experiment-missing: {CONSUMER}", file=sys.stderr)
        return 1

    build = run(["dotnet", "build", "-c", "Debug"])
    if build.returncode != 0:
        tail = "\n".join(build.stdout.splitlines()[-15:])
        if re.search(r"NU1\d{3}|unable to load the service index|Unable to find package", build.stdout):
            print("precompilation-experiment-skip: roslyn nightly feed unreachable")
            return 0
        print("precompilation-experiment-FAIL: build error\n" + tail, file=sys.stderr)
        return 1

    proc = run(["dotnet", "run", "-c", "Debug", "--no-build"])
    out = proc.stdout
    if proc.returncode != 0:
        print("precompilation-experiment-FAIL: run error\n" + proc.stderr, file=sys.stderr)
        return 1

    failures: list[str] = []
    for label, (pattern, want) in EXPECTED.items():
        m = re.search(pattern, out)
        got = m.group(1) if m else "<none>"
        if got != want:
            failures.append(f"{label}: expected {want!r}, got {got!r}")

    for b in EXPECTED_BINDINGS:
        if b not in out:
            failures.append(f"missing compile-time-only binding: {b}")

    if failures:
        print("precompilation-experiment-FAIL:\n  " + "\n  ".join(failures), file=sys.stderr)
        print("\n--- actual output ---\n" + out, file=sys.stderr)
        return 1

    print("precompilation-experiment-ok (37 contract symbols bound; 5 compile-time-only attributes inferred)")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
