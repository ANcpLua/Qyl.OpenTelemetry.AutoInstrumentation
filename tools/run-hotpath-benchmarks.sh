#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS="${TMPDIR:-/tmp}/qyl-benchmarkdotnet-artifacts"

rm -rf "$ARTIFACTS"

dotnet run \
  -c Release \
  --project "$ROOT/benchmarks/Qyl.AutoInstrumentation.Benchmarks/Qyl.AutoInstrumentation.Benchmarks.csproj" \
  -- \
  --filter '*' \
  --artifacts "$ARTIFACTS"

mkdir -p "$ROOT/docs/benchmarks"
cp "$ARTIFACTS/results/"*-report-github.md "$ROOT/docs/benchmarks/"

python3 "$ROOT/tools/verify-benchmark-report.py"
