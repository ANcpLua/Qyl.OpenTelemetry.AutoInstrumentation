# qyl-dotnet-autoinstrumentation

Free, **vendor-neutral .NET zero-code instrumentation runtime — NativeAOT-ready**.

**Architecture rule (v0.2.0 substrate swap):** C# owns ALL behavior, *and* the runtime is now a
pure-managed AOT-native library — no native CLR profiler, no IL rewriting, no startup-hook
attach. The substrate is **Roslyn source generators + `DiagnosticListener` subscriptions +
`[ModuleInitializer]`**. `Qyl.AutoInstrumentation.Hosting` ships a build-transitive consumer
module initializer that roots the qyl bootstrap from a plain `PackageReference`; explicit
`AddQylAutoInstrumentation()` calls use the same idempotent boot path.

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

## Analyzer + NativeAOT proof

The repo build is gated by the requested analyzer stack:

- `ANcpLua.Analyzers` `2.0.2`
- `Microsoft.CodeAnalysis.Analyzers` `5.3.0`
- `ErrorProne.NET.CoreAnalyzers` `0.8.2-beta.1`
- `ErrorProne.NET.Structs` `0.6.1-beta.1`
- `Roslynator.Analyzers` `4.15.0`
- `JonSkeet.RoslynAnalyzers` `1.0.0-beta.6`

`Microsoft.CodeAnalysis.CSharp` is pinned to `5.3.0` for the build-time source-generator project.
Runtime projects inherit `IsAotCompatible`, trim, AOT, and single-file analyzers with
`TreatWarningsAsErrors=true`. The source-generator project is intentionally different: it targets
`netstandard2.0` and runs only inside the compiler. Normal repo builds use it as an analyzer;
`PublishAot=true` excludes the generator project so NativeAOT publish never tries to publish a
build-time Roslyn assembly.

Current evidence proves the runtime closure is NativeAOT-clean and emits spans under NativeAOT:

- `demos/Qyl.LiveInstrumentationDemo` publishes as `net10.0`/`osx-arm64` with
  `PublishAot=true` and captures `http.client`, `http.server`, `db.efcore`, `db.sqlclient`,
  and `rpc.grpc` qyl spans.
- A temporary consumer with only a `PackageReference` to `Qyl.AutoInstrumentation.Hosting` and no
  explicit qyl startup call publishes with `PublishAot=true` and captures a qyl HttpClient span via
  the build-transitive bootstrap.

The formal Gate A golden-OTLP normalizer and Gate B no-behavior-change baseline are still tracked
in `COVERAGE_LEDGER.md`.

## Projects

| Project | TFM | Role |
|---|---|---|
| `Qyl.AutoInstrumentation` | net10.0 | API surface: `QylActivitySource`, `QylSelfTelemetry`, `QylInstrumentation.Activate()`, source-gen-fed `QylSemConvRegistry`. |
| `Qyl.AutoInstrumentation.SourceGenerators` | netstandard2.0 | `IIncrementalGenerator` that emits the semconv `FrozenSet<string>` at compile time. |
| `Qyl.AutoInstrumentation.DiagnosticListeners` | net10.0 | One `DiagnosticListener` subscriber per instrumented library (HttpClient, AspNetCore, EFCore, SqlClient, gRPC). |
| `Qyl.AutoInstrumentation.Hosting` | net10.0 | Build-transitive consumer bootstrap + `[ModuleInitializer]` auto-boot + `IServiceCollection.AddQylAutoInstrumentation()`. |

The three runtime projects build under `TreatWarningsAsErrors=true` with `IsAotCompatible`,
`IsTrimmable`, `EnableTrimAnalyzer`, `EnableAotAnalyzer`, and `EnableSingleFileAnalyzer` all on.
The source-generator project is build-time-only and explicitly excluded from NativeAOT publish, so
an accidental runtime reflection or publish-graph regression fails the build instead of deployment.

## Status

- **M1 walking skeleton (new substrate)** — *in progress*: NativeAOT package-reference boot and
  qyl span emission are proven; Gate A (golden span) + Gate B (no-behavior-change) still need the
  formal checked-in gate runner.

The substrate-era M1–M12 are preserved in `COVERAGE_LEDGER.md` under the *archived* section
and remain reproducible from the `v0.1.0-archive` tag.
