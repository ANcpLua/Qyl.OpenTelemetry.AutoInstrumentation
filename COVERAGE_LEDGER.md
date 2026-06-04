# qyl Auto-Instrumentation — Coverage Ledger & Milestone Gates

> Operating contract (locked):
> 1. **Total coverage** — every blueprint box has a row + status here. A box with no row is a bug.
>    `out-of-scope` is permitted *only* with a written reason. Silent drops are forbidden.
> 2. **Per-milestone gates** — a milestone is `proven` only when BOTH gates are green.
>    The next milestone MUST NOT start until the prior is `proven`.
>
> Decision locked (v0.2.0-pre.1 substrate swap): **runtime = pure-managed library**, AOT-native.
> Source generators + `DiagnosticListener` subscriptions + `[ModuleInitializer]` replace the
> external CLR profiler / IL-rewriting substrate that backed M1–M12 of the v0.1.0 series.

## Gate definitions (unchanged)

| Gate | Name | Pass condition |
|------|------|----------------|
| A | Golden-OTLP | Emitted signals → canonical OTLP, volatile fields normalized (TraceId/SpanId/timestamps/durations/host.*), diffed against checked-in golden. **Zero semantic diff.** |
| B | No-behavior-change | App run WITH vs WITHOUT a reference to `Qyl.AutoInstrumentation.Hosting`: identical stdout, stderr, exit code, exceptions (thrown+caught), return values. **App-invisible.** |

Gate B is captured **baseline-first**: the WITHOUT-reference run is recorded *before* any
`PackageReference` is added.

## Status legend
`proven` done+gated · `M1`/`M{n}` scheduled to that milestone · `in-progress` · `decided` (design fixed, no code) ·
`reuse` (inherited from BCL primitives — qyl writes none) · `cross-cutting` (enforced by every gate) ·
`backlog` (has a home, not yet scheduled — assigned to a milestone before work starts) ·
`archived` (proven under v0.1.0 substrate, see `v0.1.0-archive` tag) ·
`oos:<reason>` (explicitly dropped, reason required)

## Milestones (post-swap)

| Milestone | Goal | Gate A golden | Gate B baseline | State |
|-----------|------|---------------|-----------------|-------|
| **M1 AOT walking skeleton** | A NativeAOT-published consumer app, with a `PackageReference` to `Qyl.AutoInstrumentation.Hosting`, emits ONE HttpClient CLIENT span via `QylActivitySource` to a console listener — driven by the `HttpHandlerDiagnosticListener` subscription. | one CLIENT span: method/url/server | app stdout/exit identical w/wo the reference + 0 spans in the control arm | **in-progress** — PackageReference zero-code NativeAOT boot + HttpClient span emission proven locally; formal Gate A/B runner still pending. |
| M2+ | *not enumerated until M1 is `proven`* (the principle) | — | — | — |

## Coverage ledger — blueprint §00–§09 + T000–T032 (re-aimed)

| Ref | What | Status |
|-----|------|--------|
| §00 LANGUAGE_OWNERSHIP | C# owns behavior; AOT-compatible code only | `decided` |
| §00 DO_NOT_WRITE_IN_CSHARP | COM/ICorProfiler/ReJIT/IL native boundary | `oos: substrate-swap — qyl no longer attaches via the profiler API` |
| §01 EXISTING_CODE_REUSE | BCL `ActivitySource` / `Meter` / `DiagnosticSource` | `decided` |
| §02 REPO_SKELETON | 4-project solution + Directory.Build.props + .slnx | **proven** ✅ (this commit) |
| §03 ATTACHMENT_SURFACE | build-transitive consumer bootstrap + `[ModuleInitializer]` + `AddQylAutoInstrumentation()` | `in-progress` (PackageReference boot proven locally; formal Gate B pending) |
| §04 ARCHITECTURE | layer reference model | `decided` (source-gen + listener + module-init triad) |
| §05 TASK_CHAIN | the 33 chains | tracked below |
| §06 SEMCONV_COVERAGE | full attribute/metric/span registry | `in-progress` (build-time FrozenSet via source generator) |
| §07 INSTRUMENTATION_MODULES | per-library coverage | `in-progress` (live demo captures HttpClient, ASP.NET Core, EFCore, SqlClient, gRPC with safe semantic attributes; formal gates pending) |
| §08 GOLDEN_OUTPUT_SHAPES | SpanData/MetricData/LogRecordData schemas | `M1` (defines Gate A normalizer) |
| §09 DONE_STATE | final exit criteria | `decided` (the finish line — unchanged from v0.1.0) |
| T000 establish baseline | env/runtime/AOT publish matrix | `M1` (osx-arm64/net10 cell first) |
| T001 reuse decision | BCL primitives + source-gen | `proven` (this commit) |
| T002 repo bootstrap | solution/packages/CI | `M1` (solution proven ✅; CI `backlog`) |
| T003 semconv generation | Weaver → `FrozenSet<string>` | `in-progress` (skeleton generator wired) |
| T004 native profiler boundary | CLSID/protocol/events | `oos: substrate-swap` |
| T005 startup hook | `Initialize()` entrypoint | `oos: replaced by [ModuleInitializer]` |
| T006 loader | ALC resolver + activate managed profiler | `oos: not needed under AOT — direct PackageReference` |
| T007 configuration | env/file/precedence/redaction | `M1` (console listener only) / rest `backlog` |
| T008 resource detection | service/host/os/process/k8s/cloud | `backlog` |
| T009 provider bootstrap | Tracer/Meter/Logger providers | `M1` (Tracer via ActivityListener) / Meter+Logger `backlog` |
| T010 rule manifest | data-defined rule compiler | `backlog` (differentiator) |
| T011 calltarget ABI | begin/end/exception/async handlers | `oos: substrate-swap — DiagnosticListener replaces this` |
| T012 source instrumentation runtime | ActivitySource/DiagnosticListener subs | `in-progress` (central semantic policy + live domain proof; formal Gate A/B pending) |
| T013 bytecode instrumentation runtime | ReJIT rewrite pipeline | `oos: substrate-swap — incompatible with AOT` |
| T014 http server | ASP.NET Core | `in-progress` (real Kestrel 204 + unhandled-exception 500 paths proven under managed, NativeAOT, and PackageReference zero-code; formal Gate A/B pending) |
| T015 http client | HttpClient | `M1` (real `HttpHandlerDiagnosticListener` 503 + connection-error paths proven under managed, NativeAOT, and PackageReference zero-code; formal Gate A/B pending) |
| T016 grpc/rpc | Grpc.Net | `in-progress` (live qyl span demo; formal Gate A/B pending) |
| T017 database | ADO.NET/EFCore/SqlClient | `in-progress` (EFCore + SqlClient live qyl span demo; formal Gate A/B pending) |
| T018 cache | StackExchange.Redis | `backlog` |
| T019 messaging | Kafka/RabbitMQ/Azure/AWS/GCP | `backlog` |
| T020 cloud sdk | Azure/AWS/GCP SDKs | `backlog` |
| T021 faas | Lambda/Functions/GCF | `backlog` |
| T022 object stores | S3/Blob/GCS | `backlog` |
| T023 graphql | GraphQL.NET | `backlog` |
| T024 logging | ILogger/log4net/NLog/Serilog | `backlog` |
| T025 runtime/process | runtime+process metrics | `backlog` |
| T026 feature flags | flag evaluation events | `backlog` |
| T027 genai | OpenAI/Anthropic/Bedrock/Azure AI | `backlog` (differentiator) |
| T028 mcp | client/server spans + transports | `backlog` (differentiator) |
| T029 exceptions | span/log exception events | `backlog` |
| T030 profiles | pprof/CPU/alloc/wall profiles | `oos: OTel profiling signal pre-stable; revisit when spec ships GA` |
| T031 security/safety | no-behavior-change, sensitive-off-by-default | `cross-cutting` (enforced by Gate B every milestone) |
| T032 test matrix | runtimes × OS/arch × hosts | `cross-cutting` (gates run per-cell; new M1 = osx-arm64/net10 only) |

## Archived (v0.1.0 substrate) milestones

The following 12 milestones are `archived` — proven under the v0.1.0 substrate (CLR profiler +
`OTEL_DOTNET_AUTO_PLUGINS`) and reproducible from tag `v0.1.0-archive`. They are preserved here
as the historical audit trail; their *gates* (Gate A / Gate B) port forward unchanged, their
*mechanism* does not.

| Milestone | Goal (v0.1.0) | State |
|-----------|---------------|-------|
| M1 Walking Skeleton (substrate-era) | One HttpClient CLIENT span via substrate attach | `archived` ✅ proven osx-arm64/net8 |
| M2 First qyl code (substrate-era) | qyl plugin via `OTEL_DOTNET_AUTO_PLUGINS` | `archived` ✅ |
| M3 Generated registry (substrate-era) | Reflection over `Qyl.OpenTelemetry.SemanticConventions` (922 keys) | `archived` ✅ |
| M4 Self-telemetry (substrate-era) | `qyl.semconv.attribute.checks` counter | `archived` ✅ (counter contract preserved in new `QylSelfTelemetry`) |
| M5 Unknown path (substrate-era) | `verdict=unknown` on real data | `archived` ✅ |
| M6 Logs signal (substrate-era) | ILogger record with trace correlation | `archived` ✅ |
| M7 Gate runner (substrate-era) | `spike/gate.sh` | `archived` ✅ (mechanism: substrate attach) |
| M8 First qyl instrumentation (substrate-era) | GenAI span | `archived` ✅ |
| M9 MCP span (substrate-era) | `mcp.*` client span | `archived` ✅ |
| M10 Enforcement (substrate-era) | `gate.sh --strict` | `archived` ✅ |
| M11 Distributable tool (substrate-era) | `qyl install` deploys into substrate store | `archived` ✅ (entire mechanism `oos` under AOT) |
| M12 Breadth fan-out (substrate-era) | DB/messaging/RPC spans through gate.sh | `archived` ✅ |
