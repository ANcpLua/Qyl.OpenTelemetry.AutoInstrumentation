# Changelog

Notable changes to `Qyl.OpenTelemetry.AutoInstrumentation`. Versions track the Qyl.OpenTelemetry
stack line and are owned by `<Version>` in `Directory.Build.props`. CI packs that exact version,
proves the indexed packages in clean managed and NativeAOT consumers, and only then creates the
matching `v*` tag and GitHub release.

## [8.0.0] - 2026-07-19

Intentional breaking convergence plus version-pinned telemetry paths. The new AI,
MCP, and CoreWCF entries below describe only the exact library versions and hooks
exercised by repository evidence; they are not provider- or protocol-wide claims.
The exact `ModelContextProtocol` 1.4.1 client/server path has strict NativeAOT
evidence; the other new paths have managed evidence only.

### Breaking changes

- **BREAKING:** the generated-code ABI anchor moved from
  `QylGeneratedCodeAbi.V6` to `QylGeneratedCodeAbi.V8`. Generated interceptors now
  require the V8 runtime anchor, so mixing an 8.x generator with a 6.x runtime (or
  the reverse) fails compilation.
- **BREAKING:** deleted the orphan `QylInterceptedWcfCore` generated-code helper and
  its unused policy/domain/name tail. No generator called it; CoreWCF server spans
  now use the official `CoreWCF.Primitives` `ActivitySource` path.
- **BREAKING:** deleted the generated HttpWebRequest, ASP.NET endpoint-map,
  EF Core, Azure client, and `MeterProviderBuilder.AddMeter` interceptor lanes and
  their unused generated-code helpers. Runtime listeners, specialist packages,
  explicit SDK meter registration, and first-party Azure sources are now the single
  owners of those signals. The HttpClient and gRPC client interceptor lanes remain
  the call-site owners of outbound HTTP/gRPC spans, header/metadata capture, and
  URL redaction; their completion listeners defer per signal ownership.
- **BREAKING:** deleted the DiagnosticListeners package's synthetic `qyl.db.efcore` and
  `qyl.db.sqlclient` demo listeners. Real EF Core and Microsoft.Data.SqlClient events
  are owned only by their dependency-isolating specialist packages.
- **BREAKING:** deleted the custom HTTP duration producer; the
  `System.Net.Http/http.client.request.duration` instrument is authoritative. The
  NServiceBus qyl meter is now `Qyl.OpenTelemetry.AutoInstrumentation.NServiceBus`,
  and `Qyl.Sdk` no longer force-registers the library-native `Npgsql`,
  `NServiceBus.Core`, and `NServiceBus.Core.Pipeline.Incoming` meters — consumers
  that want those instruments exported register them via
  `QylSdkOptions.AdditionalMeters`. MCP metrics are likewise outside the 8.0.0
  contract (mcp spans are registered; the `Experimental.ModelContextProtocol` meter
  is consumer-registered).
- **BREAKING:** renamed the public diagnostic extension base
  `DiagnosticListenerSubscriber` to `QylDiagnosticListenerSubscriber`. The five
  concrete ASP.NET Core, EF Core, gRPC client, HttpClient, and SqlClient listener
  types are internal; the abstract qyl-prefixed subscriber remains the supported
  extension surface.

### Changed

- Every emitted interceptor now carries one adjacent machine-readable JSON manifest
  containing its interceptor kind, signal, instrumentation ID, additional metric
  IDs, and canonically derived contract keys. Contract verification reads emitted
  generated output instead of reconstructing ownership from generator source text,
  and proves that every remaining catalog kind appears in the checked artifact.
- Azure SDK instrumentation now uses the SDK's first-party `Azure.*`
  `ActivitySource` path. Bootstrap enables `Azure.Experimental.EnableActivitySource`;
  `Qyl.Sdk` subscribes the wildcard and normalizes the bounded qyl domain/name/error
  contract before export.
- The `Qyl.Sdk` → Hosting → core NuGet dependency chain now preserves build and
  analyzer assets. A clean consumer references only `Qyl.Sdk`, executes `AddQyl`,
  requires an emitted interceptor, and runs the same payload as managed code and
  NativeAOT.
- `Qyl.Sdk` registers the following version-pinned, environment-switchable telemetry paths:
  - `Microsoft.Extensions.AI` 10.8.0 traces and metrics through the application's
    explicit `UseOpenTelemetry()` chat-client wrapper;
  - `Microsoft.Agents.AI` 1.13.0 traces and metrics through the application's
    explicit `UseOpenTelemetry()` agent wrapper;
  - `Microsoft.Agents.AI.Workflows` 1.13.0 traces through the application's explicit
    `WithOpenTelemetry()` workflow hook;
  - `ModelContextProtocol` 1.4.1 automatic official client/server traces, verified
    on the exact path under both managed execution and strict NativeAOT; and
  - `CoreWCF.Http` 1.9.1 managed server traces from `CoreWCF.Primitives`.
- The new signal-specific IDs are `MICROSOFTEXTENSIONSAI`, `MICROSOFTAGENTSAI`,
  `MICROSOFTAGENTSAIWORKFLOWS`, and `MCP`; CoreWCF uses `WCFCORE`. Each follows the
  standard `OTEL_DOTNET_AUTO_{SIGNAL}_{ID}_INSTRUMENTATION_ENABLED` switch shape.
- MCP is traces-only in 8.0. Its metrics are deliberately not registered because the
  official instruments attach dynamic tool and resource names as dimensions, which
  conflicts with qyl's bounded-cardinality policy.
- Direct OpenAI SDK instrumentation, raw Anthropic SDK instrumentation,
  `Azure.AI.Inference`, Amazon Bedrock, and A2A are not claimed by 8.0.

## [6.0.0] - 2026-07-18

Intentional pre-consumer convergence release: the last cheap breaking window
before launch. No compatibility shims; no external consumers existed for 5.x.

### Changed

- **BREAKING:** the generated-code ABI is now isolated in the
  `Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode` namespace: the 21
  `QylIntercepted*` runtime helpers and `QylMetricMeters` moved there, are
  hidden from completion (`EditorBrowsable(Never)`), and are deliberately
  versioned — generated interceptors reference the `QylGeneratedCodeAbi.V6`
  anchor constant, which is renamed on every future ABI break so mismatched
  generator/runtime package pairs fail to compile instead of misbehaving.
- **BREAKING:** everything that is neither the documented user bootstrap API
  nor generated-code ABI is now internal: `QylSemanticAttributes`,
  `QylActivityNames`, `QylActivitySource`, `QylAutoInstrumentationIds`,
  `QylAutoInstrumentationOptions`, `QylDbClientMetrics`, `QylInstrumentation`,
  `QylInstrumentationDomains`, `QylMetricNames`, `QylNServiceBusMetrics`.
  First-party sibling packages consume them via `InternalsVisibleTo`; demos
  were rewritten to use only the public surface plus the generated semconv
  packages, proving the public API suffices for real consumers.
- **BREAKING:** generated code no longer references
  `QylAutoInstrumentationOptions` or `QylInstrumentationDomains`: the GraphQL
  document opt-in is enforced solely at its runtime control point
  (`QylSensitiveCapturePolicy.SetGraphQlDocument`), and external-logger domain
  values are emitted as literals (identical IL — consts inline).
- **BREAKING:** `InterceptorTarget` derives contract keys structurally from
  `TelemetrySignal` + `InstrumentationId`; freeform `"signals.*"` key strings
  are unrepresentable in the generator and rejected by the gate.
- Interceptor body descriptors emit themselves (abstract `Emit` member); the
  emitter's runtime-type switch is gone and body-type exhaustiveness is
  enforced by the compiler.
- The contract-invariants gate was rebuilt zero-based (evidence or deletion):
  every surviving check carries an executed mutation proof or a cited external
  contract; internal member-name substring pins were deleted, so internal
  renames no longer break the gate.
- Public API baselines collapsed: `PublicAPI.Shipped.txt` now records the
  final 6.0.0 surface (core: 333 → 157 entries, including the stale removed
  `QylSelfTelemetry` rows), `PublicAPI.Unshipped.txt` is empty.

### User-facing API after convergence

- `Qyl.OpenTelemetry.AutoInstrumentation.Hosting`: `Boot()`,
  `AddQylAutoInstrumentation(...)`, `QylAutoInstrumentationHostingOptions`.
- `Qyl.Sdk`: `AddQyl(...)`, `QylSdkOptions`.
- Core: `AddQylAspNetCoreInstrumentation()`, `QylAutoInstrumentationSignal`.
- DiagnosticListeners: the public listener/subscriber surface (unchanged).
- Configuration remains environment-variable driven per the coverage matrix.

## [5.0.0] - 2026-07-13

### Changed

- **BREAKING:** removed the Telemetry Capability Graph API that leaked through the 4.1.0 core
  package, along with its never-published `.Publishing` companion project. A library-wide catalog
  was not evidence of the telemetry emitted by a particular consumer binary.
- **BREAKING:** removed the development conformance listener and its hosting/options surface; it
  changed sampling and had no product consumer.
- **BREAKING:** stopped binding ASP.NET classic capture/redaction and SQLClient .NET Framework IL
  rewrite options that this managed .NET 10/NativeAOT implementation cannot apply.
- Removed the unused 60-row contract manifest that the analyzer emitted into every consumer and
  the receiver-pattern projection that only fed the deleted graph; repository verification now
  compares actual generator target keys directly with the owned YAML.
- Replaced the generic ADO.NET demo's hand-written command double with a real in-memory SQLite
  database invoked through the provider-neutral `DbConnection`/`DbCommand` surface.
- Replaced hand-shaped OTLP JSON and protobuf substring scanning with a loopback receiver that
  decodes `ExportTraceServiceRequest` using the official OpenTelemetry protobuf package.

## [4.1.0] - 2026-07-13

### Changed

- Deps: `Qyl.OpenTelemetry.SemanticConventions` (+`.Incubating`) bumped 3.0.2 → 3.4.0 (registry
  1.41-era → **1.43.0**), restoring the workspace-wide version lockstep. Six `db.system.name`
  well-known values (`Elasticsearch`, `Mongodb`, `OracleDb`, `OtherSql`, `Redis`, `Sqlite`)
  repointed to the `.Incubating` assembly — semconv 3.3.0 removed the non-registry-stable enum
  members from the stable assembly. Wire values are unchanged; no emitted telemetry differs.
- **BREAKING (generator internals):** structural descriptor invariants — the self-referential
  validation apparatus is deleted (#28).
- Build: 30-demo matrix split into `Demos.slnx` (#36); SDK pinned to 10.0.301 with the MTP test
  runner.
- CI: all workflows on GitHub-hosted runners (#31); one live run per ref + job timeouts (#29);
  NativeAOT-publish warning-cleanliness gate (17 clean / 13 vendor-warned) (#27); aot-gate
  satisfies the `PublishAot` analyzer contract instead of redirecting artifacts (#30); real-demos
  on x64 — mssql/server ships no arm64 image (#32).
- Tools: local TCG-publishing verifier registered in the goal gate (#37); `artifacts/publish`
  dropped after passing verify gates (#35).
- Docs/chore: comment truth sweep (#33); agent-state purge (#34).

## [4.0.3] - 2026-07-01

### Fixed

- Semconv completeness: `url.scheme`, `http.request.method_original`, Azure operation
  (CODE RED #6/#7/#9) (#25).
- Build: `QylInstrumentation.Version` (the OTel instrumentation-scope version stamped on every
  emitted span/metric) is now baked from the build `<Version>` via a generated compile-time const
  instead of a hardcoded literal — no reflection. A new `verify-version-sync` gate keeps the props
  floor, the README examples, and that scope version aligned with the latest release tag (#21).

### Docs

- README pins the vocabulary to the official OpenTelemetry glossary (Instrumented Library /
  Instrumentation Library / Instrumentation Scope / Automatic Instrumentation / Semantic
  Conventions) and maps each term onto this repository, framing the AOT source-interceptor +
  `DiagnosticListener` mechanism as OpenTelemetry "Automatic Instrumentation". All three signals
  (traces, metrics, logs) are contract-covered; AOT-structural items are marked
  `unsupported_nativeaot`, not hidden (#19).
- Corrected the stale README install-example package version (`0.3.0-pre.1` → `4.0.0`) (#19).

## [4.0.2] - 2026-07-01

### Changed

- **BREAKING:** single-owner-per-signal registry — ends double instrumentation when multiple
  registration paths cover the same signal (CODE RED #3) (#23).

## [4.0.1] - 2026-07-01

### Fixed

- Real span duration, honest doc, and the correct thread-count instrument
  (CODE RED #1/#2/#10) (#22).

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

- **Telemetry Capability Graph experiment.** `TelemetryCapabilityGraphGenerator` bakes the
  library contract into the core assembly as `QylTelemetryCapabilityGraph`. The repository also
  added an unpublished `Qyl.OpenTelemetry.AutoInstrumentation.Publishing` experiment and local
  demo; that package was not part of the five-package NuGet release (#12, #13, #14).

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
