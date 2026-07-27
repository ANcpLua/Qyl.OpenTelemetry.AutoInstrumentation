#!/usr/bin/env bash
# G1 vocabulary smoke (cheap CI cross-check, not the gate — QYL0200 is authoritative).
#
# Greps for '"qyl.' string literals in the producer-family package source (src/).
# Tests, demos, tools, build assets, and generated files (*.g.cs) are outside the
# scope by the gate's construction.
#
# Expected result: zero hits. A hit means hand-written vocabulary — move the name
# into the registry (qyl-registry.json in the semconv repo) and reference the
# generated constant.
set -euo pipefail
cd "$(dirname "$0")/.."

hits=$(grep -rn '"qyl\.' --include='*.cs' src 2>/dev/null \
  | grep -v '\.g\.cs:' \
  | grep -v '/obj/\|/bin/' \
  || true)

if [[ -n "$hits" ]]; then
  echo "G1 vocabulary smoke FAILED — hand-written \"qyl.* literals in producer-family scope:" >&2
  echo "$hits" >&2
  exit 1
fi

echo "G1 vocabulary smoke passed: 0 hits in scope."
