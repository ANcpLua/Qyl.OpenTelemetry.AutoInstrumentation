# Changelog

Notable changes to `Qyl.OpenTelemetry.AutoInstrumentation`. Versions track the Qyl.OpenTelemetry
stack line; releases are tag-driven (`v*`) and CI-published. The `<Version>` in
`Directory.Build.props` is the local-build floor and is CI-overridden from the release tag at pack
time.

## [Unreleased]

- Build: `QylInstrumentation.Version` (the OTel instrumentation-scope version stamped on every
  emitted span/metric) is now baked from the build `<Version>` via a generated compile-time const
  instead of a hardcoded literal — no reflection. A new `verify-version-sync` gate keeps the props
  floor, the README examples, and that scope version aligned with the latest release tag.
- Docs: README pins the vocabulary to the official OpenTelemetry glossary (Instrumented Library /
  Instrumentation Library / Instrumentation Scope / Automatic Instrumentation / Semantic
  Conventions) and maps each term onto this repository, framing the AOT source-interceptor +
  `DiagnosticListener` mechanism as OpenTelemetry "Automatic Instrumentation". All three signals
  (traces, metrics, logs) are contract-covered; AOT-structural items are marked
  `unsupported_nativeaot`, not hidden (#19).
- Docs: corrected the stale README install-example package version (`0.3.0-pre.1` → `4.0.0`) (#19).

## [4.0.0] - 2026-07-01

### Changed

- **BREAKING:** dropped the `WebApplicationBuilder.Build()` interceptor. The ASP.NET Core
  server-request middleware is now registered via an `IStartupFilter` through
  `AddQylAspNetCoreInstrumentation()`, removing the cross-generator coordination layer and its
  opt-out property; middleware span semantics (request/response header + query-string capture) are
  preserved (#20).
- Build: added the `core.slnf` solution filter (core packages, no demo projects) (#18).

## [3.1.2] - 2026-06-30

### Fixed

- Generator no longer intercepts `RequestDelegate.Invoke`; delegate-invocation call-sites produced
  CS9207 (#17).

## [3.1.1] - 2026-06-30

### Changed

- Generator opts out of `WebApplicationBuilder.Build()` interception (#16).
- Generator: dropped dead `InterceptorTarget` mirrors and un-shadowed factory helpers (#15).

## [3.1.0] - 2026-06-29

### Added

- **Telemetry Capability Graph (First-Light, steps 1–3).** `TelemetryCapabilityGraphGenerator`
  bakes the compile-time TCG into the core assembly as `QylTelemetryCapabilityGraph`
  (`.Json` / `.SchemaVersion` / `.CapabilityCount`), and the
  `Qyl.OpenTelemetry.AutoInstrumentation.Publishing` package emits it as a true OTel `LogRecord` at
  host startup via `ILogger` (no OTel SDK dependency), proven by `demos/Qyl.RealTcgPublishingDemo`
  (#12, #13, #14).

### Fixed

- Restored the green NativeAOT goal-gate floor and the slim-builder AOT demo (#10).

## [3.0.2] - 2026-06-24 — initial public release

First release under the canonical name, rebaselining the pre-public `0.x` line (the orphaned
`v0.*-pre` tags are skipped by the publish bump regex).

### Changed

- **Canonical rename** `Qyl.AutoInstrumentation` → `Qyl.OpenTelemetry.AutoInstrumentation`, with the
  package version aligned onto the Qyl.OpenTelemetry stack line at 3.0.2.
- Migrated to Central Package Management; onboarded Renovate with the shared preset.
- Split the Roslyn generator into detection / shapes / descriptors partials (no generated-output
  change).

### Added

- Apache-2.0 `LICENSE` (OpenTelemetry-ecosystem standard, incl. patent grant).
- Pure-managed, NativeAOT-ready runtime substrate: core APIs, source-generated
  `[InterceptsLocation]` interceptors, DiagnosticListener subscribers, `build/`/`buildTransitive/`
  assets, and `[ModuleInitializer]` activation. Hosting / EFCore / SqlClient packages isolate heavy
  dependencies and their AOT-warning boundaries.
- Real consumer demos (managed + NativeAOT where the library supports it): HttpClient, ASP.NET Core,
  EFCore, gRPC, and Microsoft.Data.SqlClient, plus Confluent.Kafka, RabbitMQ.Client, MongoDB.Driver,
  StackExchange.Redis, Quartz, and MassTransit against real Docker-backed brokers/servers.
  NServiceBus 9.2.11 is a documented managed-only boundary (endpoint creation requires
  Reflection.Emit).
- Behavioral verification gates: package layout, ProjectReference behavior, public API baselines,
  XML docs, environment options, conformance, source-generator snapshots, source-interceptor
  behavior, smoke, WebAPI AOT, and OTLP verified/collector fixtures.

### Telemetry semantics

- Adopted the upstream OpenTelemetry .NET privacy model and removed the
  `QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES` option: `url.full` is always emitted on client
  spans and `url.path` / `url.query` on server spans, with query values redacted per key
  (`?token=Redacted`, keys stay); the `OTEL_DOTNET_EXPERIMENTAL_*_DISABLE_URL_QUERY_REDACTION` flags
  only switch redacted to raw. `db.namespace` is always emitted; `db.query.text` sits solely behind
  the upstream `SET_DBSTATEMENT_FOR_TEXT` flag. Bootstrap sets
  `System.Net.Http.DisableUriRedaction` so the BCL does not collapse query strings to `*` before qyl
  redacts per value.
- Added the upstream `OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES` option on the source-generated
  `MeterProviderBuilder.AddMeter(...)` path (exact meter-name case preserved, de-duplicated against
  built-ins), and expanded built-in meter registration (ASP.NET Core framework meters,
  `System.Net.NameResolution` for HTTP client, and the NServiceBus incoming-pipeline meter).
- Span names are OTel-semconv-shaped low-cardinality values composed by `QylActivityNames`
  (`{method}` / `{method} {route}` / `{rpc.service}/{rpc.method}` / `DB {operation}`), not fixed
  literals.

---

## Continuation notes

Useful-for-continuation direction, not a commit dump:

- Finish an OTLP export normalizer so the verified-OTLP gate validates real exported telemetry
  end-to-end, not only OTLP-shaped committed fixtures.
- Turn BenchmarkDotNet evidence into stable allocation/latency budgets once CI runner noise is
  characterized.
- Add a single update path for the OpenTelemetry .NET auto-instrumentation contract that regenerates
  the coverage matrix, generator contract data, and snapshots together.
- Expand source-generator interception only for source-visible, semantically stable call-sites — do
  not chase hidden binary internals with reflection or runtime patching.
