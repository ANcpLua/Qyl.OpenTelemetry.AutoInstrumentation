# qyl-dotnet-autoinstrumentation

Best-ever free, **vendor-neutral .NET zero-code instrumentation runtime**.

**Architecture rule:** C# owns ALL behavior. The native CLR profiler is a *reused, replaceable
substrate* (OTel/Datadog-derived, behind `AutoInstrumentation.NativeBridge`) — never reinvented.
Reuses the OpenTelemetry SDK. Semantic conventions are generated from the OTel registry by the
qyl semconv source generator.

**Method:** milestone-gated. Every milestone passes BOTH gates before the next starts:
- Gate A — Golden-OTLP (emitted signals match a checked-in golden, volatile fields normalized)
- Gate B — No-behavior-change (app stdout/stderr/exit/exceptions identical with vs without attach)

Coverage is tracked in `COVERAGE_LEDGER.md` — every blueprint box has a status; nothing is
silently dropped (`out-of-scope` requires a written reason).

## Status
- **M1 walking skeleton — PROVEN** (osx-arm64 / net8): attach to an unmodified app → one
  HttpClient CLIENT span to the console exporter; app behavior byte-identical; span attributable
  (0 spans in the control arm). Golden: `spike/golden/m1.client-span.golden.txt`.
- **M2 — first qyl-authored code** (proposed): a qyl plugin loaded via `OTEL_DOTNET_AUTO_PLUGINS`
  asserting emitted attribute keys ∈ qyl semconv registry.
