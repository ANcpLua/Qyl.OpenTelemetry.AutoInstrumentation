# Ultra-Emitter backlog — autonomous execution queue

Execute top to bottom. One item = one or more commits, full validation battery green
between items. The Codex goal prompt carries the LAWS; **this file carries the work**.
Re-read this file at the start of the session and again after each completed item.

Every design fork below is already decided — do **not** re-open them. If reality
contradicts a "ground truth" anchor here, STOP and report instead of guessing.

## Ground truth (verified 2026-06-13 @ 21f504a)

- Version: `0.3.0-pre.1` (`Directory.Build.props:43`). Target after this backlog: 0.4.0 (items 1–3), 0.5.0 (rest). Do not bump/tag — that is the human's release cut.
- Evidence reality (`docs/coverage-matrix.md`): 33 `implemented` promises, all verified — **30 `verified_nativeaot` + 3 `verified_managed`**; plus 23 `option_bound`, 4 `none` (all lane `unsupported_nativeaot`). The old "only 4 verified" figure was stale goal text — ignore it.
- Catalog: `src/Qyl.AutoInstrumentation.SourceGenerators/QylGeneratedSourceInterceptorCatalog.g.cs` (~70 lines) holds `CreateGeneratedMatcherDescriptors()` + `CreateGeneratedEmissionDescriptors()`, consumed at `QylAutoInstrumentationGenerator.cs:39–42`.
- Catalog renderer: `tools/generate-contract-artifacts.py` → `render_interceptor_catalog_cs` (~line 1061) emits that `.g.cs` from **hardcoded C# lines as Python string literals**; the contract is used only as a missing-key gate (`GENERATED_INTERCEPTOR_CATALOG_REQUIRED_SIGNAL_KEYS`).
- `InterceptorEmissionDescriptor` is a `private readonly record struct` (`QylAutoInstrumentationGenerator.cs:3903`) with **8 default-sentinel body slots**: `TraceBody, ForwardingBody, HttpWebRequestBody, DbCommandBody, GrpcClientBody, MeterProviderBuilderBody, LoggerBody, ExternalLoggerBody` (each `= default`, exactly one set per descriptor).
- Options re-read smell: `QylInterceptedHttpClient.cs` → `StartHttpClientObservation` takes `options` (~line 576) but re-reads `QylAutoInstrumentationOptions.Current` (~line 631).
- `QylSemConvRegistry.cs:39` `static partial void Contribute(...)` is filled by `SemConvRegistryGenerator.cs:143`.
- Matcher shape expectations are imperative in `TryGet*Invocation`: Redis `RedisKey` first-param (~3265), Kafka `string|TopicPartition` (~3193), RabbitMQ `ValueTask` return (~2493), HttpClient `HttpRequestMessage|string|Uri` (~3032–3110).
- Provenance convention: matrix rows 45 & 48 (`SET_DBSTATEMENT_FOR_TEXT`) **intentionally** dual-source a legacy 60-item link + a current upstream removal note. Not a bug.

---

## Item 1 — Evidence residual (classification only, ~15 min)

The big bucketing job is already done (all 33 implemented are verified). Residual:
1. Find the 3 `verified_managed` promises in `docs/coverage-matrix.md`. For each, decide and record one of: **promote** (name the existing `verify-real-*` harness that could carry a NativeAOT proof — do NOT build it here) or **managed_only_by_design** (state the blocking technical fact).
2. Confirm each of the 4 `none`/`unsupported_nativeaot` rows carries a one-line blocking fact; add any that is missing.
3. Record decisions in the contract source of truth (trace the canonical input from `tools/generate-contract-artifacts.py`; do not guess the file), `--write`, battery, commit.
4. Set `docs/OPEN-QUESTIONS.md` #2 → `answered:<commit>`.

Fence: classification/docs only — no demos, descriptors, emitters.

---

## Item 2 — B1: catalog SSOT back to C#, Python becomes verifier-only (~1 h)

**Why:** `render_interceptor_catalog_cs` is a copy machine — product C# embedded as Python
string literals: refactor-blind, IDE-blind, type errors only surface at regen+compile.

**Decided direction (do not re-open):** the descriptors bind to **C# delegates**
(`TryGet*Invocation`) and **typed constructors** — they are *code*, not data. So C# is their
correct home. This is NOT "Python bad": polyglot generation is right when the source is real
data (the semconv vocabulary already uses Weaver + `.j2` templates — leave that alone). The
catalog is the exception because it is code-coupled. Python keeps doing what it is good at
here: **validation**.

Steps:
1. Create `src/Qyl.AutoInstrumentation.SourceGenerators/QylAutoInstrumentationGenerator.Catalog.cs` — a plain hand-owned partial-class file (no auto-generated header) holding the exact descriptor arrays currently in the `.g.cs`. Rename `CreateGeneratedMatcherDescriptors`→`CreateMatcherDescriptors`, `CreateGeneratedEmissionDescriptors`→`CreateEmissionDescriptors`; update the two call sites at `:39–42`.
2. Delete `QylGeneratedSourceInterceptorCatalog.g.cs`; delete `render_interceptor_catalog_cs` and its `--write`/`--check` wiring for that file only.
3. **Invert the gate into verification:** extend `verify-contract-invariants.py` (it already greps C# — follow its pattern) to assert bidirectionally: every `implemented` source-interceptor contract key appears in the catalog file, and every contract key referenced in the catalog exists in the contract as `implemented`. Fail with the exact missing/extra sets.
4. **Do NOT touch test oracles.** `EXPECTED_VERIFIED` in `verify-source-interceptor-consumer.py:493` (and similar expected-output blocks) are *test assertions*, not the copy-machine — they are sharp every run and must stay. Only the catalog renderer dies.

Fence: zero descriptor content/order/semantics change — relocation only. Snapshots
byte-identical. Set `docs/OPEN-QUESTIONS.md` #1 → `answered:<commit>`.

---

## Item 3 — B5: runtime options-flow policy + dead-surface sweep (~1 h)

**Policy (apply, do not debate):** `QylAutoInstrumentationOptions.Current` is read **exactly
once per intercepted operation, at the interception entry point**, then threaded as a
parameter. Helpers never touch `.Current`. A threaded `options` that ends up unused is
**deleted**, not kept.

Steps:
1. Fix the verified case: `QylInterceptedHttpClient.StartHttpClientObservation` — use the threaded `options`, remove the `.Current` re-read (~line 631).
2. Sweep all runtime helpers in `src/Qyl.AutoInstrumentation`, `.Hosting`, `.EntityFrameworkCore`, `.SqlClient` for the same two smells (options threaded-but-unused; `.Current` read >once per op): `QylInterceptedAspNetCore`, `QylInterceptedHttpWebRequest`, `QylInterceptedLogger`, `QylInterceptedMongoDb`, `QylDbClientMetrics`, `QylRuntimeProcessMetrics`, and every other `QylIntercepted*`/`Qyl*Metrics`. Apply the policy uniformly.
3. Prove the `Contribute` wiring: `QylSemConvRegistry.cs:39` `static partial void Contribute` must be implemented by `SemConvRegistryGenerator.cs`. Inspect the csproj analyzer wiring + the `obj/` generated output after a build to confirm it runs on every project that compiles `QylSemConvRegistry`. If not wired where needed → wire it. If empty-in-some-compilations is intended → add one comment at the partial naming the generator and why empty is correct. Prove with build output, do not guess.
4. Rename the `InterceptorTarget` positional param `Parameters` (it hides an outer method; suggestion `ParameterSpecs`) and update usages. Generator-internal → snapshots stay byte-identical.
5. Final analyzer pass: zero unused-parameter/unused-symbol findings in the four runtime projects, fixed at root cause (wire or delete).

Fence: no telemetry behavior change (snapshots + touched `verify-real-*` demos prove it); no
new options/env vars/API. Public signature break is fine — update PublicAPI baselines + run
`verify-public-api-baseline.py`, no shims. Set `docs/OPEN-QUESTIONS.md` #4 and #5 →
`answered:<commit>`.

---

## Item 4 — B2: polymorphic body-descriptor hierarchy (~1.5 h, snapshots are everything)

**Why:** `InterceptorEmissionDescriptor` carries 8 mutually-exclusive default-sentinel body
slots — a type hierarchy in disguise.

**Critical caution:** the descriptor is a `record struct` whose default-sentinel slots give it
value equality. Before introducing a reference-type hierarchy, determine whether
`InterceptorEmissionDescriptor` flows through the **incremental generator pipeline**
(equality-cached) or is only used in the static catalog (equality-irrelevant). If it is
pipeline-cached, preserve value-equality (custom `Equals`/`IEquatable`) or the generator's
caching breaks silently. Byte-identical snapshots are the oracle regardless.

Steps:
1. Introduce abstract `InterceptorBodyDescriptor` (source-generators project, stay in the netstandard2.0 / existing-polyfill envelope) exposing **one** emit entry point whose signature is what the current per-slot emitters need (the `InterceptorTarget`, the `StringBuilder`, naming helpers). Derive one sealed subclass per existing slot: `TraceInterceptorBodyDescriptor`, `ForwardingInterceptorBodyDescriptor`, `HttpWebRequestBodyDescriptor`, `DbCommandBodyDescriptor`, `GrpcClientBodyDescriptor`, `MeterProviderBuilderBodyDescriptor`, `LoggerBodyDescriptor`, `ExternalLoggerBodyDescriptor`.
2. `InterceptorEmissionDescriptor` loses the 8 slots, gains one non-optional `Body` of the abstract type. The emitter's per-kind dispatch collapses into a virtual call. Each subclass's emit code is **moved, not rewritten** — same strings, same order, same whitespace.
3. Update the catalog construction (now plain C# from Item 2).
4. Migrate fewest-usages slot first, trace body last. One slot per commit, full battery between.

Fence: output bytes are sacred — if preserving them forces an ugly intermediate, keep it and
note it. No matcher (`TryGet*`) / runtime / tools changes. Generator-internal, no public API.

---

## Item 5 — B3: declarative matcher shapes (~1.5 h)

**The MongoDB lesson:** emission is never hardcoded (it comes from the consumer symbol), but
*matching* encodes signature expectations imperatively. These expectations are irreducible
auto-instrumentation knowledge — **relocate them into auditable data, don't remove them**.

Steps:
1. Add a declarative `MethodExpectation` record to the matcher data model: method-name set, receiver requirement, first-param type alternatives (e.g. `RedisKey|RedisKey[]`, `string|TopicPartition`, `HttpRequestMessage|string|Uri`), return-type requirement (`Task`/`Task<T>`/`ValueTask`/`void`/any-emittable), extension-receiver-reduction flag.
2. Build **one** shared matching engine interpreting `MethodExpectation` against an `IMethodSymbol` via the existing helpers (`IsType`, `IsArrayOf`, `IsTask`, `IsValueTask`, `TryGetTaskResult`, `TryGetReducedExtensionReceiverType`, …), producing the `InterceptorTarget` exactly as the `TryGet*` methods do today.
3. Migrate easiest-first: MongoDB (name-list only), Quartz, GraphQL, MassTransit, Redis, Kafka, RabbitMQ, logging family, HttpClient family. Per library: express expectations as data, route through the engine, **delete the dead imperative code in the same commit**.
4. **Escape hatch — use it, don't force-fit:** if a library genuinely doesn't fit the model (likely: gRPC streaming, AspNetCore builder/endpoint, DbCommand provider mapping, Azure wildcard receivers), leave its `TryGet*` imperative, mark its descriptor `imperative-matcher`, and list it in the report with one reason line. A 70% declarative / 30% honestly-imperative split beats a forced abstraction.

Fence: expectations must not loosen or tighten — proven by byte-identical snapshots + green
`verify-real-*` for every migrated library. No emission/runtime changes.

---

## Item 6 — B4: zero-match diagnostic (~1 h)

**Why:** on a driver version bump, signature drift means the interceptor is silently not
emitted → the span just vanishes. `verify-real-*` demos only cover pinned versions.

**Severity (decided):** Info by default; elevated to Warning **only** when the consumer sets
MSBuild `QylWarnOnZeroMatch=true`. Consumers run `TreatWarningsAsErrors` — a default Warning
could break a stranger's build over a false positive. Default Info is mandatory.

Steps:
1. Each matcher descriptor gains its anchor type's metadata name(s) (e.g. `MongoDB.Driver.IMongoCollection`1`, `StackExchange.Redis.IDatabase`); wildcard matchers (`Azure.*Client`) use the assembly-name prefix and a descriptor flag instead.
2. After the discovery pass, for each instrumentation where the anchor type resolves via `compilation.GetTypeByMetadataName` (package referenced) AND zero call-sites matched, raise a Roslyn diagnostic (new `QYL` id — find the repo's existing id scheme first): "Instrumentation '<name>' is active and '<package>' is referenced, but no interceptable call-sites were found. Likely: the driver API changed in a newer version, calls aren't source-visible, or the library is referenced but unused."
3. Elevate to Warning via `QylWarnOnZeroMatch` flowed through AnalyzerConfig options like existing build-asset properties (copy the pattern the interceptor-namespace property uses); wire through `build/`/`buildTransitive/`; validate with `verify-package-layout.py`.
4. Exclude structurally-always-matching builder/wildcard matchers (AspNetCore `Build()`, MeterProviderBuilder) via descriptor flag, not a hardcoded name list.
5. Prove fire / no-fire / elevate with consumer-fixture cases (extend `verify-source-interceptor-consumer.py` or the snapshot fixture — follow the existing pattern; new verified files are additive, never edit existing baselines). Document the id + property in README (short, operational).

Fence: default severity must not break any consumer build; no runtime/telemetry change. Set
`docs/OPEN-QUESTIONS.md` #3 → `answered:<commit>`.

---

## Item 7 — B6: split the generator + consolidate the ratchets (~45 min)

Done late on purpose — the body hierarchy (Item 4) + matching engine (Item 5) must exist so
the cut lines follow real responsibilities.

Part 1 — file split (pure moves, only `partial` plumbing): break the ~3.8k-line
`QylAutoInstrumentationGenerator.cs` into partials by responsibility — pipeline/`Initialize`,
`.Catalog.cs` (exists), `.Matching.cs`, `.Emission.cs`, one file per large body family, and a
`.Model.cs` (`InterceptorTarget`, `ParameterSpec`, `MethodExpectation`, enums). Identical code,
same namespace/accessibility. Proven by byte-identical snapshots.

Part 2 — ratchet consolidation: inventory every string-grep ratchet in
`verify-contract-invariants.py`; replace the family with a structured checker that reads each
target file once and evaluates a declarative rule table (rule id, target, assertion, message).
Every assertion keeps its exact strength — list any that can't be preserved 1:1 and STOP for
that rule. Prove equivalence by temporarily breaking one rule's target locally, confirming the
red, reverting (do not commit the probe). CLI/exit-code behavior stays CI-compatible.

Fence: no semantic changes anywhere.

---

## Item 8 — B7: docs/VALIDATION.md (~30 min, documentation only)

Current-tree reality only — no aspirational prose. Where reality is unclear, add an
OPEN-QUESTIONS row instead of guessing.

For each gate (`generate-contract-artifacts.py --check/--write`, `verify-contract-invariants`,
`verify-contract-coverage-report`, `verify-generator-snapshots`,
`verify-source-interceptor-consumer`, `verify-projectreference-behavior`,
`verify-package-layout`, `verify-public-api-baseline`, `smoketest.sh`, the OTLP fixtures, the
`verify-real-*-demo` family, `verify-aot-autoinstrumentation-goal`, and the `.github/workflows`
runner mapping) document exactly four things: **PURPOSE** (1 sentence), **INPUTS & ORACLE**
(what it reads, what it compares against, where the baseline lives — for snapshots describe the
full source→output→`verified/` chain precisely), **A GREEN RUN PROVES**, **A GREEN RUN DOES NOT
PROVE** (e.g. snapshots prove emission shape for fixture call-sites, not runtime correctness;
real demos prove pinned driver versions, not future ones; the zero-match diagnostic covers
referenced-but-drifted, not unreferenced).

Also document the matrix provenance convention (rows 45/48 dual-source legacy + current — state
what each link proves). Close with a "gap map" linking OPEN-QUESTIONS #7–#11. Set #6 →
`answered:<commit>`; add the synthetic CHANGELOG summary of this whole backlog.

Fence: no code/tool changes; README stays user-facing.
