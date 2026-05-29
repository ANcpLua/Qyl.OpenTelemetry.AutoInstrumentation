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
| **M1 Walking Skeleton** | Attach to unmodified app → ONE HttpClient CLIENT span reaches console exporter | one CLIENT span: method/url/server (`golden/m1.client-span.golden.txt`); status_code/error.type dropped as outcome-dependent | `spike` app stdout/exit identical w/wo attach + 0 spans in control arm | **PROVEN ✅** osx-arm64 / net8. Re-proven after hardening the fixture to be deterministic + network-independent (output decoupled from HTTP outcome). Proves substrate + gate-harness + platform; HttpClient = reused OTel instr. |
| **M2 First qyl code** | A qyl-authored plugin in the live pipeline (via `OTEL_DOTNET_AUTO_PLUGINS`) asserts every emitted attribute key ∈ qyl semconv registry | M1 golden span UNCHANGED | app-observable identical + control arm still 0 spans | **PROVEN ✅** qyl plugin executes; 6/6 emitted keys OK; verdict side-channelled (0 stderr bytes); span + app behavior unchanged. Registry is a PLACEHOLDER HashSet (→ M3). |
| **M3 Generated registry** | Replace the plugin's placeholder key set with the GENERATED semconv constants — promotes the generator to a runtime-enforced invariant | M1 golden span UNCHANGED; synthetic unknown key flagged UNKNOWN | app-observable identical | **PROVEN ✅** registry = `Qyl.OpenTelemetry.SemanticConventions` 3.0.0 (stable + Incubating) via NuGet, reflected at runtime = **922 keys** (was 7). Self-test: real key known, synthetic unknown → discriminates. Gate A/B unchanged, 0 stderr bytes. |
| **M4 Self-telemetry** | Surface conformance as qyl-owned telemetry via a qyl Meter → counter `qyl.semconv.attribute.checks{verdict}` — first use of the METRICS pipeline (M1–M3 traces-only) | M1 trace golden UNCHANGED + metric golden (counter present, verdict=ok, Value=key count) | app-observable identical | **PROVEN ✅** qyl Meter `Qyl.AutoInstrumentation` → `qyl.semconv.attribute.checks` (LongSum) exported via metrics console; verdict=ok Value=6; metric ABSENT in control arm (attributable); Gate A/B unchanged, 0 stderr bytes. |
| **M5 Unknown path (real data)** | Prove the `verdict=unknown` metric slice end-to-end on a REAL non-semconv key | M1 trace golden UNCHANGED; metric shows verdict=unknown ≥ 1 | app-observable identical | **PROVEN ✅** m5app fixture emits a custom span with `qyl.custom.unmapped` (∉ registry) via `OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES`; metric verdict=unknown=1 AND verdict=ok=1; attributable (0 control); M1 re-checked still PASS. No plugin code change — M4 code already handled the unknown branch. |
| **M6 Logs signal** | Third pillar: prove the LOGS pipeline — an `ILogger` record flows with trace correlation | M1 trace golden UNCHANGED + log-record golden (body + trace_id present) | app-observable identical | **PROVEN ✅** m6app logs inside an active span; substrate-injected OTel logger captures it; LogRecord body+Severity present; log TraceId == span TraceId (correlated); absent in control; 0 stderr bytes. THREE-PILLAR plumbing (traces+metrics+logs) complete. |
| **M7 Gate runner** | Consolidate deploy + control/attach + golden-diff + conformance into a reusable `spike/gate.sh` | M1–M6 all re-pass via the script | n/a (tooling) | **PROVEN ✅** `spike/gate.sh <name> <csproj> <marker> [--plugin --metrics --logs --sources]` reproduces m1 (traces), m5 (metrics+conformance 1 OK/1 UNKNOWN), m6 (logs+trace-correlation) — all PASS, 0 stderr. Bespoke bash retired. Bug found+fixed: `set -u` breaks sourcing the substrate's instrument.sh. |
| **M8 First qyl instrumentation** | Breadth begins: a qyl-authored GenAI span (`gen_ai.*`), conformance-checked + gated via gate.sh | M1 trace golden UNCHANGED + golden for the domain span | app-observable identical | **PROVEN ✅** m8app emits CLIENT span "chat gpt-4" with 6 gen_ai.* attrs; gate.sh: GateB PASS, spans 1/0, 6/6 OK, metric verdict=ok=6. Golden: `golden/m8.genai-span.golden.txt`. |
| **M9 MCP span** | Second differentiator: a qyl-authored `mcp.*` client span on a frontier domain | M1 trace golden UNCHANGED + golden | app-observable identical | **PROVEN ✅** m9app "tools/call" CLIENT span; gate.sh GateB PASS, spans 1/0; MIXED verdicts on REAL data — 3 OK (`mcp.method.name`, `mcp.session.id`, `rpc.system`) / 2 UNKNOWN (`mcp.tool.name`, `mcp.transport`); metric ok=3 unknown=2. Real finding: semconv 1.41 has PARTIAL MCP coverage. |
| **M10 Enforcement** | Turn conformance from observation into a GATE: `gate.sh --strict` fails when verdict=unknown > 0 | n/a (tooling) | n/a | **PROVEN ✅** `--strict`: m8 (GenAI, 0 unknown) → strict=PASS exit 0; m9 (MCP, 2 unknown) → strict=FAIL(2unk) exit 1. Conformance loop complete: observe (M2–M3) → measure (M4–M5) → ENFORCE (M10). |
| **M11 Distributable tool** | Package qyl as a `dotnet tool` (`qyl install`) that deploys the plugin into the substrate store — removes the manual cp + env juggling | tool deploys + a fixture passes via the tool-deployed plugin | n/a | **PROVEN ✅** `src/qyl.AutoInstrumentation.Cli` packs `Qyl.AutoInstrumentation.Cli` 0.1.0 (bundles plugin + 2 semconv DLLs; whitelist excludes `OpenTelemetry.dll`); `dotnet tool install` → `qyl install` deploys 9 files across net8/9/10; GenAI fixture via the tool-deployed plugin → Gate B PASS, 6 OK. |
| **M12 Breadth fan-out (§07)** | Scale coverage: DB, messaging, RPC domain spans as parallel fixtures through gate.sh | M1 trace golden UNCHANGED + breadth golden | app-observable identical | **PROVEN ✅** dbapp (5 OK), msgapp (4 OK), rpcapp (4 OK) — all GateB PASS, spans 1/0, 0 unknown. Golden: `golden/m12.breadth.golden.txt`. 6 domains total: HTTP/GenAI/MCP/DB/Msg/RPC. |
| M13+ | next: CI pipeline · bytecode (§T013) · more §07 modules · NuGet-publish | — | — | **awaiting direction** |

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
