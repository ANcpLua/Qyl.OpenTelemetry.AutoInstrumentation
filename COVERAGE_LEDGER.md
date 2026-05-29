# qyl Auto-Instrumentation — Coverage Ledger & Milestone Gates

> Operating contract (locked):
> 1. **Total coverage** — every blueprint box has a row + status here. A box with no row is a bug.
>    `out-of-scope` is permitted *only* with a written reason. Silent drops are forbidden.
> 2. **Per-milestone gates** — a milestone is `proven` only when BOTH gates are green.
>    The next milestone MUST NOT start until the prior is `proven`.
>
> Decision locked: **native profiler = OTel/Datadog-derived substrate, reused** (replaceable behind
> `AutoInstrumentation.NativeBridge`). **Managed layer = clean-room qyl.** Reuse OTel SDK + qyl semconv generator.

## Gate definitions

| Gate | Name | Pass condition |
|------|------|----------------|
| A | Golden-OTLP | Emitted signals → canonical OTLP, volatile fields normalized (TraceId/SpanId/timestamps/durations/host.*), diffed against checked-in golden. **Zero semantic diff.** |
| B | No-behavior-change | App run WITH vs WITHOUT qyl attached: identical stdout, stderr, exit code, exceptions (thrown+caught), return values. **App-invisible.** |

Gate B is captured **baseline-first**: the WITHOUT-attach run is recorded *before* any attach exists.

## Status legend
`proven` done+gated · `M1`/`M{n}` scheduled to that milestone · `in-progress` · `decided` (design fixed, no code) ·
`reuse` (inherited from substrate, qyl writes none) · `cross-cutting` (enforced by every gate) ·
`backlog` (has a home, not yet scheduled — assigned to a milestone before work starts) ·
`oos:<reason>` (explicitly dropped, reason required)

## Milestones (defined one at a time — per your principle, M2+ is NOT enumerated until M1 is `proven`)

| Milestone | Goal | Gate A golden | Gate B baseline | State |
|-----------|------|---------------|-----------------|-------|
| **M1 Walking Skeleton** | Attach to unmodified app → ONE HttpClient CLIENT span reaches console exporter | one CLIENT span: method/url/server + status (`golden/m1.client-span.golden.txt`) | `spike` app stdout/exit identical w/wo attach + 0 spans in control arm | **PROVEN ✅** osx-arm64 / net8. NOTE: proves substrate + gate-harness + platform; **no qyl-authored managed code yet** (HttpClient = reused OTel instr). |
| **M2 First qyl code** | A qyl-authored plugin in the live pipeline (via `OTEL_DOTNET_AUTO_PLUGINS`) asserts every emitted attribute key ∈ qyl semconv registry (wires T003 into runtime) | M1 golden span UNCHANGED | app-observable identical + control arm still 0 spans | **proposed** (awaiting go) |
| M3+ | defined only after M2 proven | — | — | locked |

## Coverage ledger — blueprint §00–§09 + T000–T032

| Ref | What | Status |
|-----|------|--------|
| §00 LANGUAGE_OWNERSHIP | C# owns behavior; native owns CLR mechanics | `decided` |
| §00 DO_NOT_WRITE_IN_CSHARP | COM/ICorProfiler/ReJIT/IL native boundary | `reuse` (OTel substrate) |
| §01 EXISTING_CODE_REUSE | SDK / exporters / OTel instrumentation pkgs | `decided` |
| §02 REPO_SKELETON | solution + project layout | `M1` (minimal: StartupHook+Loader+1 instr) |
| §03 ATTACHMENT_SURFACE | env-var attach matrix | `M1` (CORECLR_* + DOTNET_STARTUP_HOOKS + console exporter) / rest `backlog` |
| §04 ARCHITECTURE | layer reference model | `decided` (reference) |
| §05 TASK_CHAIN | the 33 chains | tracked below |
| §06 SEMCONV_COVERAGE | full attribute/metric/span registry | `in-progress` (qyl semconv generator) / rest `backlog` |
| §07 INSTRUMENTATION_MODULES | per-library coverage | `backlog` (one per future milestone) |
| §08 GOLDEN_OUTPUT_SHAPES | SpanData/MetricData/LogRecordData schemas | `M1` (defines Gate A normalizer) |
| §09 DONE_STATE | final exit criteria | `decided` (the finish line) |
| T000 establish baseline | env/runtime/profiler/OS matrix | `proven` (osx-arm64 cell) |
| T001 reuse decision | substrate + SDK choices | `proven` |
| T002 repo bootstrap | solution/packages/CI | `M1` (solution + spike) / CI `backlog` |
| T003 semconv generation | Weaver → constants | `in-progress` |
| T004 native profiler boundary | CLSID/protocol/events | `reuse` |
| T005 startup hook | `Initialize()` entrypoint | `M1` |
| T006 loader | ALC resolver + activate managed profiler | `M1` (minimal) |
| T007 configuration | env/file/precedence/redaction | `M1` (service name + console exporter only) / rest `backlog` |
| T008 resource detection | service/host/os/process/k8s/cloud | `backlog` |
| T009 provider bootstrap | Tracer/Meter/Logger providers | `M1` (Tracer only) / Meter+Logger `backlog` |
| T010 rule manifest | data-defined rule compiler | `backlog` (differentiator) |
| T011 calltarget ABI | begin/end/exception/async handlers | `reuse` for M1 / clean-room ABI `backlog` |
| T012 source instrumentation runtime | ActivitySource/DiagnosticListener subs | `M1` (one source) / rest `backlog` |
| T013 bytecode instrumentation runtime | ReJIT rewrite pipeline | `backlog` (engine `reuse`) |
| T014 http server | ASP.NET Core/Framework | `backlog` |
| T015 http client | HttpClient | `M1` (THE skeleton instrumentation) |
| T016 grpc/rpc | Grpc.Net + AspNetCore | `backlog` |
| T017 database | ADO.NET/EFCore/Npgsql/Mongo/… | `backlog` |
| T018 cache | StackExchange.Redis | `backlog` |
| T019 messaging | Kafka/RabbitMQ/Azure/AWS/GCP | `backlog` |
| T020 cloud sdk | Azure/AWS/GCP SDKs | `backlog` |
| T021 faas | Lambda/Functions/GCF | `backlog` |
| T022 object stores | S3/Blob/GCS | `backlog` |
| T023 graphql | GraphQL.NET | `backlog` |
| T024 logging | ILogger/log4net/NLog/Serilog | `backlog` (console exporter plumbing is `M1`) |
| T025 runtime/process | runtime+process metrics | `backlog` |
| T026 feature flags | flag evaluation events | `backlog` |
| T027 genai | OpenAI/Anthropic/Bedrock/Azure AI | `backlog` (differentiator) |
| T028 mcp | client/server spans + transports | `backlog` (differentiator) |
| T029 exceptions | span/log exception events | `backlog` |
| T030 profiles | pprof/CPU/alloc/wall profiles | `oos: OTel profiling signal pre-stable; revisit when spec ships GA` |
| T031 security/safety | no-behavior-change, sensitive-off-by-default | `cross-cutting` (enforced by Gate B + redaction defaults every milestone) |
| T032 test matrix | runtimes × OS/arch × hosts | `cross-cutting` (gates run per-cell; M1 = osx-arm64/net8 only) |
