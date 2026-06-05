#!/usr/bin/env python3
from __future__ import annotations

import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "benchmarks" / "Qyl.AutoInstrumentation.Benchmarks" / "Qyl.AutoInstrumentation.Benchmarks.csproj"
REPORTS = {
    "HttpClientHotPathBenchmarks-report-github.md": [
        "DirectGetAsync",
        "InterceptedGetAsync",
    ],
    "DbCommandHotPathBenchmarks-report-github.md": [
        "DirectSqlClientCommand",
        "InterceptedSqlClientCommand",
    ],
    "EntityFrameworkCoreHotPathBenchmarks-report-github.md": [
        "DirectExecuteSqlRaw",
        "InterceptedExecuteSqlRaw",
    ],
}

ZERO_ALLOCATION_METHODS = {
    "DbCommandHotPathBenchmarks-report-github.md": "InterceptedSqlClientCommand",
    "EntityFrameworkCoreHotPathBenchmarks-report-github.md": "InterceptedExecuteSqlRaw",
}


def run(command: list[str]) -> None:
    completed = subprocess.run(command, cwd=ROOT, check=False)
    if completed.returncode != 0:
        raise SystemExit(f"{' '.join(command)} failed with exit code {completed.returncode}")


def require(condition: bool, message: str) -> None:
    if not condition:
        raise SystemExit(message)


def verify_report(report: Path, methods: list[str]) -> None:
    require(report.exists(), f"missing benchmark report: {report}")
    text = report.read_text(encoding="utf-8")
    for token in ("BenchmarkDotNet", "NativeAOT", ".NET 10.0", "Mean", "Allocated"):
        require(token in text, f"{report.name} missing token: {token}")
    for method in methods:
        require(method in text, f"{report.name} missing benchmark method: {method}")
    if method := ZERO_ALLOCATION_METHODS.get(report.name):
        lines = [line for line in text.splitlines() if line.startswith(f"| {method} ")]
        require(len(lines) == 2, f"{report.name} expected two runtime rows for {method}")
        for line in lines:
            require("|         - |" in line, f"{report.name} {method} is not zero-allocation: {line}")


def main() -> None:
    run(["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet"])
    run(["dotnet", "run", "-c", "Release", "--project", str(PROJECT), "--", "--smoke"])

    for file_name, methods in REPORTS.items():
        verify_report(ROOT / "docs" / "benchmarks" / file_name, methods)

    print("benchmark-report-ok")


if __name__ == "__main__":
    main()
