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

Current evidence proves the qyl runtime closure is NativeAOT-clean and emits spans under
NativeAOT. Library-specific packages call out upstream app-side warning boundaries when the
instrumented library itself is not warning-clean. The active generator direction is compile-time
interception for source-visible call-sites: no CLR profiling, no startup hooks, no runtime IL
rewriting, no reflection, and no dynamic dispatch. The first compiler-emitted interceptor delegates
source-visible `HttpClient.SendAsync` calls to the qyl runtime wrapper; the full 60-item contract
is tracked in the generated instrumentation manifest.

- `demos/Qyl.LiveInstrumentationDemo` publishes as `net10.0`/`osx-arm64` with
  `PublishAot=true` and captures `http.client`, `http.server`, `db.efcore`, `db.sqlclient`,
  and `rpc.grpc` qyl spans.
- A temporary consumer with only a `PackageReference` to `Qyl.AutoInstrumentation.Hosting` and no
  explicit qyl startup call publishes with `PublishAot=true` and captures a qyl HttpClient span via
  the build-transitive bootstrap.
- `demos/Qyl.RealEfCoreDemo` proves the EFCore-specific package against real
  `CommandExecutedEventData` and `CommandErrorEventData` payloads under managed and NativeAOT
  execution. EFCore is not warning-clean under NativeAOT in .NET 10.0.8: the proof uses the
  supported compiled-model path and intentionally demotes EFCore's own IL warnings for publish,
  then runs the native binary.
- `demos/Qyl.RealGrpcClientDemo` proves `Grpc.Net.Client` success and Unavailable failure paths
  under managed and warning-clean NativeAOT execution. It uses `WebApplication.CreateSlimBuilder`
  for the local h2c proof server and reads only AOT-safe gRPC activity tags.
- `demos/Qyl.RealSqlClientDemo` proves `Microsoft.Data.SqlClient` command success and SQL Server
  error paths under managed and NativeAOT execution. The qyl listener is warning-clean; the app
  publish intentionally demotes `Microsoft.Data.SqlClient` 7.0.1 trim/AOT warnings and must not use
  `InvariantGlobalization=true` because SqlClient throws in invariant globalization mode.
- A temporary consumer with only `PackageReference` wiring for
  `Qyl.AutoInstrumentation.EntityFrameworkCore` and no qyl startup call restored from locally
  packed nupkgs and captured `PASS name=DB INSERT`, proving the EFCore build-transitive bootstrap.
- A temporary gRPC consumer with only `PackageReference` wiring for
  `Qyl.AutoInstrumentation.Hosting` and no qyl startup call restored from locally packed nupkgs
  and captured `PASS name=gRPC qyl.LiveProbe/Collect`.
- A temporary SqlClient consumer with only `PackageReference` wiring for
  `Qyl.AutoInstrumentation.SqlClient` and no qyl startup call restored from locally packed nupkgs,
  published under NativeAOT, and captured `PASS name=SQL SELECT operation=SELECT`.

The formal Gate A golden-OTLP normalizer and Gate B no-behavior-change baseline are still tracked
in `COVERAGE_LEDGER.md`.

## Projects

| Project | TFM | Role |
|---|---|---|
| `Qyl.AutoInstrumentation` | net10.0 | API surface: `QylActivitySource`, `QylSelfTelemetry`, `QylInstrumentation.Activate()`, source-gen-fed `QylSemConvRegistry`. |
| `Qyl.AutoInstrumentation.SourceGenerators` | netstandard2.0 | `IIncrementalGenerator` that emits the semconv `FrozenSet<string>` at compile time plus compile-time interceptors for source-visible call-sites. |
| `Qyl.AutoInstrumentation.DiagnosticListeners` | net10.0 | Shared `DiagnosticListener` substrate and built-in subscribers for HttpClient, ASP.NET Core, gRPC, plus synthetic EFCore and SqlClient semantic proof events. |
| `Qyl.AutoInstrumentation.EntityFrameworkCore` | net10.0 | EFCore-specific build-transitive bootstrap and typed command payload reader. Kept out of the shared host so non-EFCore apps do not inherit EFCore package warnings. |
| `Qyl.AutoInstrumentation.SqlClient` | net10.0 | Microsoft.Data.SqlClient-specific build-transitive bootstrap and command payload reader. Kept out of the shared host so non-SqlClient apps do not inherit SqlClient package warnings. |
| `Qyl.AutoInstrumentation.Hosting` | net10.0 | Build-transitive consumer bootstrap + `[ModuleInitializer]` auto-boot + `IServiceCollection.AddQylAutoInstrumentation()`. |

The runtime projects build under `TreatWarningsAsErrors=true` with `IsAotCompatible`,
`IsTrimmable`, `EnableTrimAnalyzer`, `EnableAotAnalyzer`, and `EnableSingleFileAnalyzer` all on.
The source-generator project is build-time-only and explicitly excluded from NativeAOT publish.
Analyzer or AOT regressions fail the repo build; upstream NativeAOT warnings from EFCore and
SqlClient are documented at the app publish boundary.

## Runtime semantics

The agent emits runtime values only when the instrumented library supplied them through
`DiagnosticSource` payloads or the current `Activity`. It does not invent fallback URLs, database
names, SQL statements, routes, methods, status codes, or RPC methods.

Semantic rules live in the diagnostic listener layer:

- Stable OpenTelemetry keys are emitted; deprecated aliases such as `http.url`, `http.status_code`,
  `http.method`, `db.statement`, and `db.name` are consumed as input only.
- Sensitive raw values are off by default. `url.full`, `url.path`, and `db.query.text` are emitted
  only when `QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES=true`.
- HTTP method values are normalized to the stable well-known set; unknown methods emit `_OTHER`
  with `http.request.method_original`.
- HTTP span status follows the stable HTTP rules: client `4xx+` and server `5xx+` become
  `ActivityStatusCode.Error` and get low-cardinality `error.type`.
- Database spans prefer bounded `db.operation.name` and `db.query.summary`; raw query text remains
  privacy-gated.

`demos/Qyl.LiveInstrumentationDemo` now checks both sides of this contract: every covered domain
must emit its required safe attributes, and privacy-gated attributes must not leak with defaults.
`demos/Qyl.RealHttpClientDemo` uses real .NET `HttpClient` traffic and BCL
`HttpHandlerDiagnosticListener` events to prove runtime extraction for 503 and connection-failure
paths under managed and NativeAOT execution. `demos/Qyl.RealAspNetCoreDemo` does the same for
Kestrel/EndpointRouting via the `Microsoft.AspNetCore` listener. `demos/Qyl.RealEfCoreDemo`
does the same for EFCore command success and provider-error paths, with the EFCore compiled-model
NativeAOT prerequisite called out explicitly. `demos/Qyl.RealGrpcClientDemo` proves real
`Grpc.Net.Client` success and failure activity tags. `demos/Qyl.RealSqlClientDemo` proves
`Microsoft.Data.SqlClient` command success and SQL Server error payloads. The compile-time
interceptor scaffold now covers source-visible `HttpClient.SendAsync` call-sites. The per-library matrix lives in
`docs/RUNTIME_SEMANTICS.md`.

## Status

- **M1 walking skeleton (new substrate)** — *in progress*: NativeAOT package-reference boot and
  qyl span emission are proven; Gate A (golden span) + Gate B (no-behavior-change) still need the
  formal checked-in gate runner.

The substrate-era M1–M12 are preserved in `COVERAGE_LEDGER.md` under the *archived* section
and remain reproducible from the `v0.1.0-archive` tag.
