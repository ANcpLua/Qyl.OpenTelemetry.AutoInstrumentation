#!/usr/bin/env bash
set -euo pipefail

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
ARTIFACTS="${TMPDIR:-/tmp}/qyl-benchmarkdotnet-artifacts"

rm -rf "$ARTIFACTS"

dotnet build "$ROOT/benchmarks/Qyl.AutoInstrumentation.Benchmarks/Qyl.AutoInstrumentation.Benchmarks.csproj" -c Release -v quiet
dotnet run -c Release --project "$ROOT/benchmarks/Qyl.AutoInstrumentation.Benchmarks/Qyl.AutoInstrumentation.Benchmarks.csproj" -- --smoke
dotnet run \
  -c Release \
  --project "$ROOT/benchmarks/Qyl.AutoInstrumentation.Benchmarks/Qyl.AutoInstrumentation.Benchmarks.csproj" \
  -- \
  --filter '*' \
  --artifacts "$ARTIFACTS"

echo "hotpath-benchmarks-ok artifacts=$ARTIFACTS"
