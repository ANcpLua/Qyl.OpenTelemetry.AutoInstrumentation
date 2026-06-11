# Changelog

## Unreleased

### Telemetry semantics

- URL emission now always requires `QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES=true`;
  the upstream `OTEL_DOTNET_EXPERIMENTAL_*_DISABLE_URL_QUERY_REDACTION` flags only upgrade
  redacted to raw once emission is on. Previously the redaction flag alone emitted a fully
  unredacted `url.full` (HttpClient/HttpWebRequest) and raw `url.query` (ASP.NET Core),
  bypassing the sensitive-values gate.
- Span names are now OTel-semconv-shaped low-cardinality values composed by
  `QylActivityNames` helpers instead of fixed literals: HTTP client spans use the normalized
  method (`GET`, fallback `HTTP` for `_OTHER`), HTTP server spans use `{method} {route}`,
  gRPC client spans use the full `{rpc.service}/{rpc.method}` name, and database spans use
  `DB {operation}` / `SQL {operation}`. The `QylActivityNames` string constants were removed.

### Real consumer demos

- Added real Confluent.Kafka, RabbitMQ.Client, and MongoDB.Driver demos that prove
  source-generated interceptors (producer/consumer, `BasicPublishAsync`, and
  `IMongoCollection<T>` commands) against real Docker-backed brokers under managed and
  NativeAOT runs, including pinned error-path spans and the app-side `TrimmerRootAssembly`
  boundaries for Confluent.Kafka and MongoDB.Driver.
- Added real StackExchange.Redis, Quartz, MassTransit, and NServiceBus demos completing the
  source-interceptor proof surface: Redis is AOT-warning-free, Quartz proves source-visible
  `IJob.Execute` delegation under a real scheduler (rooted for AOT), MassTransit 8.x publishes
  to real RabbitMQ under NativeAOT via an app-side source-generated `JsonSerializerContext`,
  and NServiceBus 10 is a documented managed-only boundary because its endpoint creation
  requires Reflection.Emit.

### Documentation and handoff

- Replaced the release-history-heavy README with a current operational guide for the .NET 10
  NativeAOT auto-instrumentation runtime.
- Added `AGENTS.md` as the canonical repo-local continuation contract and made `CLAUDE.md` a
  symlink to avoid duplicate agent instructions.
- Added project-local `SKILL.md` files so future agents can enter the correct product area without
  mixing runtime, source-generator, demo, benchmark, and verification concerns.
- Rewrote the coverage ledger into a neutral current-state ledger, removing stale ceremonial and
  substrate-era ceremony from the user-facing path.
- Removed stale `claude/**` workflow triggering from the WebAPI AOT demo workflow.

## 0.3.0-pre.1 - current slim-history baseline

This baseline was rebuilt into five semantic commits. The final tree is the AOT
auto-instrumentation product state after the history rewrite.

### Repository foundation

- Established the .NET 10 solution, SDK pinning, NuGet metadata, analyzer versions, and repo-wide
  AOT/trim/single-file warning floor.
- Centralized package versioning and package README inclusion in `Directory.Build.props`.

### AOT auto-instrumentation runtime

- Added the pure-managed runtime substrate: core APIs, source-generator targets,
  DiagnosticListener subscribers, build/buildTransitive assets, and module-initializer activation.
- Added the Roslyn source-generator project for contract-gated interceptors and generated semantic
  registries.
- Added package-specific surfaces for Hosting, EFCore, and Microsoft.Data.SqlClient to keep heavy
  dependencies and AOT warning boundaries isolated.
- Replaced runtime reflection discovery with build-time generated registries and explicit contract
  classification.

### Real consumer demos

- Added real demos for live synthetic coverage, HttpClient, ASP.NET Core, EFCore, gRPC, and
  Microsoft.Data.SqlClient.
- Proved managed and NativeAOT execution paths where the upstream library supports them.
- Kept EFCore compiled-model and SqlClient globalization/AOT-warning boundaries explicit.

### Behavioral verification gates

- Added package-layout, ProjectReference, public API, XML-doc, environment-option, conformance,
  source-generator snapshot, source-interceptor, smoke, WebAPI AOT, OTLP golden, OTLP collector,
  consumer behavior, and NativeAOT consumer gates.
- Added CI workflows for smoke, WebAPI AOT, and OTLP collector fixtures.
- Added BenchmarkDotNet hot-path measurements as evidence, not as shipped runtime surface.

### Runtime semantics and coverage

- Added runtime semantics documentation for stable OpenTelemetry attributes, privacy defaults,
  bounded names, and library-specific payload extraction.
- Added the 60-item OpenTelemetry .NET auto-instrumentation contract manifest and generated
  coverage matrix.
- Added RFC 0001 describing the AOT-native source-interceptor substrate and verification contract.

## Continuation plan

### Highest-value product work

- Finish a full OTLP export normalizer so Gate A validates real exported telemetry end-to-end, not
  only OTLP-shaped committed fixtures.
- Turn BenchmarkDotNet evidence into stable allocation/latency budgets once CI runner noise is
  characterized.
- Add an update command for the 60-item contract manifest that regenerates the coverage matrix,
  source-generator contract data, and snapshots in one path.
- Improve package UX: document the exact package choice per app type and add clearer build errors
  when analyzer/buildTransitive assets are missing.
- Expand source-generator interception only for source-visible, semantically stable call-sites.
  Do not chase hidden binary internals with reflection or runtime patching.

### Engineering hygiene

- Keep `main` history slim. Prefer semantic commits over checkpoint commits.
- Keep release tags aligned with final validated commits after history rewrites.
- Keep `CLAUDE.md` as a symlink to `AGENTS.md`.
- Preserve EFCore/SqlClient warning boundaries instead of hiding upstream NativeAOT issues inside
  generic packages.
- Continue using the combined goal verifier before handoff or release work.
