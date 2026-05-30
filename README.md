# qyl-dotnet-autoinstrumentation

Best-ever free, **vendor-neutral .NET zero-code instrumentation runtime — NativeAOT-ready**.

**Architecture rule (v0.2.0 substrate swap):** C# owns ALL behavior, *and* the runtime is now a
pure-managed AOT-native library — no native CLR profiler, no IL rewriting, no startup-hook
attach. The substrate is **Roslyn source generators + `DiagnosticListener` subscriptions +
`[ModuleInitializer]`**. A consuming app gets instrumentation by referencing
`Qyl.AutoInstrumentation.Hosting`; the C# compiler emits the boot call directly into the module
initializer, which NativeAOT understands natively.

Reuses the OpenTelemetry `ActivitySource` / `Meter` BCL primitives. Semantic conventions are
baked into a build-time `FrozenSet<string>` by the qyl source generator (no `Assembly.Load`
reflection — the substrate-era path was JIT-only).

**Method:** milestone-gated. Every milestone passes BOTH gates before the next starts:
- **Gate A — Golden-OTLP** — emitted signals match a checked-in golden, volatile fields normalized.
- **Gate B — No-behavior-change** — app stdout/stderr/exit/exceptions identical with vs without the
  `Qyl.AutoInstrumentation.Hosting` reference.

Coverage is tracked in `COVERAGE_LEDGER.md` — every blueprint box has a status; nothing is
silently dropped (`out-of-scope` requires a written reason).

## Substrate-swap note (v0.2.0-pre.1)

Pre-v0.2.0 the runtime was the OpenTelemetry .NET auto-instrumentation native CLR profiler,
attached via `CORECLR_PROFILER` / `OTEL_DOTNET_AUTO_PLUGINS` / a `dotnet tool` (`qyl install`)
that deployed the qyl plugin into the substrate's plugin store. That substrate gave us 12 proven
milestones — but it was fundamentally JIT-only and cannot run under NativeAOT-published apps.

This release rips out the substrate-attach surface (`qyl.AutoInstrumentation.Cli`,
`qyl.AutoInstrumentation.Plugin`, `spike/` fixtures, `gate.sh`) and replaces it with a
pure-managed library that ships as a NuGet package an app references directly. The
substrate-era milestones (M1–M12) are **archived in git history** at tag `v0.1.0-archive` for
reference.

## Projects

| Project | TFM | Role |
|---|---|---|
| `Qyl.AutoInstrumentation` | net10.0 | API surface: `QylActivitySource`, `QylSelfTelemetry`, `QylInstrumentation.Activate()`, source-gen-fed `QylSemConvRegistry`. |
| `Qyl.AutoInstrumentation.SourceGenerators` | netstandard2.0 | `IIncrementalGenerator` that emits the semconv `FrozenSet<string>` at compile time. |
| `Qyl.AutoInstrumentation.DiagnosticListeners` | net10.0 | One `DiagnosticListener` subscriber per instrumented library (HttpClient, AspNetCore, EFCore, SqlClient, gRPC). |
| `Qyl.AutoInstrumentation.Hosting` | net10.0 | `[ModuleInitializer]` auto-boot + `IServiceCollection.AddQylAutoInstrumentation()`. |

All four projects build under `TreatWarningsAsErrors=true` with `IsAotCompatible`,
`IsTrimmable`, `EnableTrimAnalyzer`, `EnableAotAnalyzer`, and `EnableSingleFileAnalyzer` all on,
so an accidental introduction of a reflection-laden code path fails the build, not the
deployment.

## Status

- **M1 walking skeleton (new substrate)** — *scheduled*: emit one HttpClient CLIENT span from
  `QylActivitySource` via the HttpClient diagnostic listener subscription, under a NativeAOT-published
  consumer app, with Gate A (golden span) + Gate B (no-behavior-change) both green.

The substrate-era M1–M12 are preserved in `COVERAGE_LEDGER.md` under the *archived* section
and remain reproducible from the `v0.1.0-archive` tag.
