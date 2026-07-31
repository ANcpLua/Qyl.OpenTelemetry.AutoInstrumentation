# Qyl telemetry auto-instrumentation contract

Owns .NET automatic instrumentation and `Qyl.Telemetry.Hosting`. It ends at the
OTLP exporter and never owns collector storage, querying, or product contracts.

Keep nullable, trimming, and NativeAOT behavior explicit. Instrumentation dispatch
is compile-time/source-generated; do not introduce reflection scanning, runtime IL
rewriting, sync-over-async, or public surfaces solely for tests. The generated-code
ABI, public API baselines, and interceptor namespace wiring are load-bearing.

Run focused tests while iterating and finish with
`python3 tools/verify-aot-autoinstrumentation-goal.py`. Read the command's own
exit status. Publication is tag-triggered CI OIDC only; published packages are
immutable.
