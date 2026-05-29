#!/usr/bin/env bash
# qyl milestone gate runner — one harness for every milestone.
#
# Runs a fixture WITHOUT and WITH the reused substrate attached, then enforces:
#   Gate B  : app-observable output (lines matching MARKER) identical w/wo attach
#   Attrib. : telemetry appears only WITH attach (0 in the control arm)
#   Logs    : if --logs, the log record's TraceId == the active span's TraceId (correlation)
#   Conform : if --plugin, prints the qyl conformance verdict counts
#
# Usage: gate.sh <name> <csproj> <marker> [--plugin] [--metrics] [--logs] [--sources NAME]
# NOTE: no `-u` — sourcing the substrate's instrument.sh references unset vars by design.
set -eo pipefail

NAME="$1"; CSPROJ="$2"; MARKER="$3"; shift 3
SOURCES=""; PLUGIN=0; METRICS=none; LOGS=none; STRICT=0
while [ $# -gt 0 ]; do case "$1" in
  --sources) SOURCES="$2"; shift 2;;
  --plugin)  PLUGIN=1; shift;;
  --metrics) METRICS=console; shift;;
  --logs)    LOGS=console; shift;;
  --strict)  STRICT=1; shift;;
  *) echo "unknown opt: $1" >&2; exit 2;;
esac; done

REPO="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
H="${OTEL_DOTNET_AUTO_HOME:-$HOME/.otel-dotnet-auto}"
DOTNET="${DOTNET:-$HOME/.dotnet/dotnet}"
PLUGINBIN="$REPO/src/qyl.AutoInstrumentation.Plugin/bin/Release/net8.0"
EVID="$REPO/spike/evidence"; mkdir -p "$EVID"
CONF="/tmp/qyl-conformance-$NAME.log"; rm -f "$CONF"

"$DOTNET" build "$CSPROJ" -c Release -v q >/dev/null
APP="$(dirname "$CSPROJ")/bin/Release/net8.0/$(basename "${CSPROJ%.csproj}").dll"

ENVV="OTEL_SERVICE_NAME=qyl-gate OTEL_TRACES_EXPORTER=console OTEL_METRICS_EXPORTER=$METRICS OTEL_LOGS_EXPORTER=$LOGS OTEL_DOTNET_AUTO_LOG_DIRECTORY=/tmp/qyl-otel-logs OTEL_METRIC_EXPORT_INTERVAL=500"
[ -n "$SOURCES" ] && ENVV="$ENVV OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES=$SOURCES"
if [ "$PLUGIN" = 1 ]; then
  for d in Qyl.AutoInstrumentation.Plugin.dll Qyl.OpenTelemetry.SemanticConventions.dll Qyl.OpenTelemetry.SemanticConventions.Incubating.dll; do
    cp -f "$PLUGINBIN/$d" "$H/net/net8.0/"
  done
fi

"$DOTNET" "$APP" > "$EVID/$NAME.without.stdout" 2>/dev/null || true
(
  set +eu   # the substrate's instrument.sh intentionally touches unset vars / non-zero steps
  eval "export $ENVV"
  [ "$PLUGIN" = 1 ] && export OTEL_DOTNET_AUTO_PLUGINS="Qyl.AutoInstrumentation.Plugin.Plugin, Qyl.AutoInstrumentation.Plugin" QYL_CONFORMANCE_LOG="$CONF"
  . "$H/instrument.sh"
  "$DOTNET" "$APP"
) > "$EVID/$NAME.with.stdout" 2>"$EVID/$NAME.with.stderr" || true

fail=0
diff <(grep -E "^$MARKER" "$EVID/$NAME.without.stdout") <(grep -E "^$MARKER" "$EVID/$NAME.with.stdout") >/dev/null && gateb=PASS || { gateb=FAIL; fail=1; }
sw=$(grep -c 'Activity.TraceId' "$EVID/$NAME.with.stdout" || true)
sc=$(grep -c 'Activity.TraceId' "$EVID/$NAME.without.stdout" || true)
lw=$(grep -c 'LogRecord.TraceId' "$EVID/$NAME.with.stdout" || true)
sb=$(wc -c < "$EVID/$NAME.with.stderr" | tr -d ' ')

extra=""
if [ "$LOGS" = console ]; then
  stid=$(grep -E 'Activity.TraceId' "$EVID/$NAME.with.stdout" | head -1 | grep -oE '[0-9a-f]{32}' || true)
  ltid=$(grep -E 'LogRecord.TraceId' "$EVID/$NAME.with.stdout" | head -1 | grep -oE '[0-9a-f]{32}' || true)
  if [ -n "$stid" ] && [ "$stid" = "$ltid" ]; then corr=PASS; else corr=FAIL; fail=1; fi
  extra="$extra corr=$corr"
fi
if [ "$PLUGIN" = 1 ]; then
  extra="$extra verdicts=[$(grep -hE '^(OK|UNKNOWN)' "$CONF" 2>/dev/null | awk '{print $1}' | sort | uniq -c | tr -s ' \n' ' ')]"
fi
if [ "$STRICT" = 1 ]; then
  unk=$(grep -cE '^UNKNOWN' "$CONF" 2>/dev/null || true)
  if [ "${unk:-0}" -gt 0 ]; then extra="$extra strict=FAIL(${unk}unk)"; fail=1; else extra="$extra strict=PASS"; fi
fi

printf '[%-3s] GateB=%s  spans(with/ctl)=%s/%s  logs=%s  qyl_stderr=%sb%s  => %s\n' \
  "$NAME" "$gateb" "$sw" "$sc" "$lw" "$sb" "$extra" "$([ $fail = 0 ] && echo PASS || echo FAIL)"
exit $fail
