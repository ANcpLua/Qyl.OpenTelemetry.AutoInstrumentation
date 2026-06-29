# Experiment: contract-as-Roslyn-symbols via `RegisterPreCompilationSourceOutput`

**Branch:** `experiment/precompilation-contract` (control = pristine `main`).
**Hypothesis:** moving qyl's contract from generator-baked data (author-time Python → checked-in
`InstrumentationContract.cs`) into compilation-native symbols emitted by Roslyn's experimental
`RegisterPreCompilationSourceOutput` simplifies the generator and unlocks compile-time-only
semantic coverage. AOT is **not** the hypothesis (qyl already solved AOT).

Status: **GATE 0 PASSED.** Phases 1–8 in progress. Verdict at bottom.

---

## GATE 0 — API callability (PASSED)

The only permitted abort was GATE 0 failing. It did not fail.

### API exists (primary-source verified)
- **dotnet/roslyn PR #83088** — "Add RegisterPreCompilationSourceOutput API for incremental
  generators", author `chsienki`, **merged 2026-05-20** into `main` (merge SHA
  `227a45faebc6441d8350ca930cbb312d7a6e6c92`).
- Public API surface (`src/Compilers/Core/Portable/PublicAPI.Unshipped.txt`, all gated by the
  `[RSEXPERIMENTAL007]` experimental attribute):
  - `IncrementalGeneratorInitializationContext.RegisterPreCompilationSourceOutput<TSource>(IncrementalValueProvider<TSource>, Action<PreCompilationSourceProductionContext, TSource>)`
  - same for `IncrementalValuesProvider<TSource>`
  - `PreCompilationSourceProductionContext.AddSource(string hintName, string source)` / `(hintName, SourceText)`
  - `IncrementalGeneratorOutputKind.PreCompilation = 16`
- Experiment id (`RoslynExperiments.cs`): `RSEXPERIMENTAL007`, url `dotnet/roslyn#83089`.

### Toolchain resolution
- The SDK pinned by `global.json` (10.0.300 → 10.0.301 installed, compiler `5.x` pre-merge) does
  **not** carry the API.
- No **stable** nuget.org `Microsoft.Net.Compilers.Toolset` release carries it either — verified by
  grepping the bundled `Microsoft.CodeAnalysis.dll` in `4.14.0`, `5.0.0`, `5.3.0`: all **absent**.
  The API is `main`-only (stable packages branch from release branches, not `main`).
- Roslyn `main` is version `5.9.0`. The dnceng **`dotnet-tools`** nightly feed
  (`https://pkgs.dev.azure.com/dnceng/public/_packaging/dotnet-tools/nuget/v3/index.json`)
  carries `Microsoft.Net.Compilers.Toolset 5.9.0-1.26324.7`, whose bundled
  `Microsoft.CodeAnalysis.dll` **contains** `RegisterPreCompilationSourceOutput` (grep-confirmed),
  and matching `Microsoft.CodeAnalysis.CSharp` / `.Common` at the same version exist on the feed.

### Empirical spike (under `spike/`, isolated from the repo build graph)
- One `IIncrementalGenerator`:
  - **pre-compilation phase** emits `Qyl.Spike.Generated.PreCompilationProbe` into the *initial*
    compilation, driven only by `AdditionalTextsProvider.Collect()` (a permitted non-compilation
    input);
  - **standard phase** resolves it with `compilation.GetTypeByMetadataName(...)` and would raise a
    build-breaking `QYLSPIKE001` error if null.
- Consumer app references **both** the pre-comp marker and the standard-phase confirmation type, so
  a green build is unfalsifiable end-to-end proof (the SDK's own compiler cannot produce those types).

Result — compiler override `Microsoft.Net.Compilers.Toolset 5.9.0-1.26324.7`:
```
CSC : warning QYLSPIKE002: GetTypeByMetadataName('Qyl.Spike.Generated.PreCompilationProbe')
      resolved — pre-compilation source IS visible to the standard phase
Build succeeded. 0 Error(s)

$ dotnet run
PreCompilationProbe.AdditionalFileCount               = 1
StandardPhaseConfirmation.ObservedAdditionalFileCount = 1
GATE-0 SPIKE: pre-compilation contract proven end to end.
```

**Confound flagged for Phase 8:** the experiment branch builds with the `5.9.0` nightly compiler;
the control (`main`) builds with the SDK 10.0.301 compiler. Any generator-time / allocation /
output-size delta therefore conflates *compiler version* with *the refactor*. Phase 8 must pin the
same toolset on the control measurement (or isolate the confound explicitly) or the numbers are not
science.

---

## What was built (hard scope)

The mission's 8 phases were scoped to the **decisive minimum** that answers the hypothesis with
real artifacts rather than argument: a full contract-as-symbols ablation on one isolated consumer
surface, plus one genuine compile-time-only coverage slice. Everything else is documented as
not-run (below), not silently implied.

- **Built** (`experiment/contract-precompilation/`, isolated like `spike/`, outside the slnx):
  - Python projects the real resolved contract (`docs/generated/qyl-aot-contract.resolved.yaml`)
    → a 37-row `qyl-contract.tsv` (this is Phase 1's "Python shrinks to producing resolved data").
  - `ContractPreCompilationGenerator` (235 lines): **pre-compilation phase** parses the TSV +
    seed file and emits `Qyl.Generated.Contract.*` symbols (per-item marker types,
    `InstrumentationCapability` records, `SemanticAttributeDescriptor` records, `ContractRegistry`,
    `SemanticSeeds`) into the initial compilation; **standard phase** binds them via
    `GetTypeByMetadataName` and runs Phase-5 inference over user DTOs.
  - Consumer fixture references both phases' symbols → green build + correct runtime output is
    end-to-end proof (`tools/verify-precompilation-experiment.py` re-asserts it).
- **Measured result** (real data): 37 contract capabilities emitted as symbols and bound in the
  standard phase (`BoundCapabilityCount = 37`); `signals.traces.ASPNET` stays
  `UnsupportedNativeAot` (the 4 unsupported items stay unsupported); 5 compile-time-only semconv
  attributes inferred from user DTO properties (`CustomerId→customer.id`, `OrderId→order.id`,
  `TenantId→tenant.id`, `CorrelationId→correlation.id`).
- **Not run / scoped out** (honest):
  - The production `QylAutoInstrumentationGenerator` was **not** refactored in place. H1 is measured
    against an isolated parallel equivalent, so "generator complexity rises" is an **argued
    projection** from (the isolated build's added machinery) + (the untouched 4945-line detection
    core), not an in-place before/after diff.
  - YAML was **not** wired as AdditionalFiles into the ~40 demos; the ~30-verifier suite,
    smoketest, and AOT-demo workflows were **not** re-run under the nightly compiler.
  - Phases 4/6/7 (per-integration `[TelemetryOperation]` types, runtime-ownership trim, AOT roots)
    not implemented — the listener map already shows their surface is tiny (below).

## Floor status (scoped, not overclaimed)

Production code is unchanged, so the production floor is green **by construction**;
`verify-package-layout.py` and `verify-contract-invariants.py` were re-run and pass
(`package-layout-ok`, `contract-invariants-ok`). The `spike/` and `experiment/` dirs sit **outside
the slnx / production build graph**, so the whole-repo build does not touch them. The full verifier
suite was **not** re-executed end-to-end. A bounded probe (below) confirms the bare nightly compiler
does not by itself break the floor.

## Measurements mapped onto the mission's own criteria

| Mission GENIUS conjunct (all required) | Result | Evidence |
|---|---|---|
| more semconv from semantic analysis than runtime extraction | **FALSE** | H2 yields ~5 (scales w/ DTO count); runtime listeners extract dozens of *values* (method, route, status, db.namespace, query.text, …) that pre-compilation cannot read. Aggregate: runtime ≫ semantic. |
| listeners shrink | **FALSE** | Only 3 attributes are static-semantic (`qyl.instrumentation.domain`, `rpc.system=grpc`, `db.system.name=mssql`); the other ~95% are genuine runtime value reads. Listeners essentially unchanged. |
| interceptor generation simpler | **FALSE** | The 4945-line call-site detection/emission core is untouched; the experiment *adds* a two-phase pipeline + parser + emit→compile→bind round-trip. |
| coverage matrix gains compile-time-only attributes | **TRUE** | H2 demonstrably adds `customer.id`/`order.id`/`tenant.id`/`correlation.id` — impossible for runtime listeners without (forbidden) reflection. |
| generator complexity **and** contract maintenance measurably drop | **NO** | Contract *data* maintenance drops (360 baked C# lines → 37 TSV rows). But generator *logic* roughly doubles and gains a hard dependency on an unshipped compiler. Net complexity rises. |

GENIUS is an explicit 5-way AND; only one conjunct holds. Multiple DREAMING triggers fire (below).

### The raw-LOC trap, defused
Naively: experiment generator 235 + polyfill 6 + TSV 37 = **278 < 360** baked lines — looks
*simpler*. That reading is wrong. The baseline's 360 lines are mostly **data** (60 item rows +
key-set arrays + counts + meter consts); its actual **logic** is ~120 lines (record def, `TryGet*`,
`EmitStringArray`, manifest emitter). The experiment's 235 lines are almost entirely **logic**
(TSV parser + two emit methods + symbol binder + DTO inference + namespace walk). So **logic roughly
doubles** while the data compresses into a TSV. The verdict rests on *mechanism*, not line count:
the experiment trades `static array + dictionary lookup` for `TSV parser + two-phase pipeline +
emit→compile→bind-back + IsExternalInit polyfill + dependency on a main-only unshipped compiler`.

### The spine: every win is separable from the experimental API
This is the decisive finding. Neither benefit needs `RegisterPreCompilationSourceOutput`:
- **H2 coverage** runs entirely in the **standard phase** over `CompilationProvider` + property
  names; the seed list is an `AdditionalText`, and `AdditionalTextsProvider` is available in the
  standard phase too. The pre-compilation round-trip for the seeds in this experiment is
  **demonstrative, not necessary**.
- **The maintenance win** (deleting the hand-maintained `InstrumentationContract.cs`) is achievable
  by having `generate-contract-artifacts.py` *emit* that `.cs` at author time — a ~20-line change,
  no experimental API. (The mission wrongly assumed Python already does this; it does not — the
  `.cs` is hand-authored and kept in sync by `verify-contract-invariants.py`.)

The API's unique power — emitting symbols into the **initial** compilation, visible to *other*
generators — solves a problem qyl does not have. qyl's contract has a **single internal consumer**
(its own generator), for which baked C# is strictly less work than emit→compile→bind-back. This is
the verbatim mission DREAMING trigger: *"the phase boundary forced so much into the standard phase
that pre-compilation bought nothing."* It fired.

### Secondary signal: bare-compiler floor probe (confound-aware)
Pinning the nightly toolset on the core runtime project (full AOT/trim/analyzer stack +
`TreatWarningsAsErrors`) built **green (0 warnings, 0 errors)** — so Roslyn 5.9.0 nightly does not
surface new analyzer diagnostics. The real costs are structural, not analyzer breakage:
`NU1507` forces package-source-mapping config for the extra feed, and — decisively — the API is
gated by `[Experimental(RSEXPERIMENTAL007)]`, **unshipped and main-only**. Forcing that onto every
consumer compilation is not viable for a shippable product, independent of any LOC count. Build-time
delta is left as a soft signal only (the nightly-vs-SDK compiler swap is a confound for it; the
structural findings above are confound-immune).

---

## VERDICT: **DREAMING** — with a genuine, separable H2 merit

Moving qyl's contract from generator-baked data into compilation-native symbols via
`RegisterPreCompilationSourceOutput` does **not** simplify the generator and is **not** how
compile-time-only coverage is unlocked:

- Generator **logic roughly doubles** (parser + two-phase pipeline + emit→compile→bind-back +
  polyfill) and gains a hard dependency on an **unshipped, main-only compiler** — non-shippable.
- **Listeners do not shrink**: ~95% of their work is runtime value extraction the API cannot touch;
  only 3 attributes are static-semantic.
- The contract has a **single internal consumer**, so the API's defining capability (symbols into
  the initial compilation / cross-generator visibility) buys qyl nothing. The phase boundary pushed
  every real win into the standard phase.
- The contract-maintenance improvement is **real but small** (one auto-synced `.cs` removed) and is
  obtainable with a 20-line Python change — no experimental API.

**The one real win is separable and worth keeping:** deterministic, reproducible, standard-phase
**compile-time semantic-convention inference** over user DTOs/routes (`CustomerId → customer.id`)
produces attributes the runtime path genuinely cannot, and it satisfies the Phase-5 "no guessing,
no non-reproducible heuristics" bar. Pursue it as an ordinary `IIncrementalGenerator` standard-phase
feature — **not** behind the experimental pre-compilation API.

"AOT improved" was explicitly not pursued and is not claimed. By the mission's own definition, with
generator complexity rising and the API buying nothing for qyl's single-consumer contract, this is
DREAMING with a documented, independently-shippable coverage idea — not GENIUS.

---

## Addendum: the 1→N reframe (composition, not AOT) — `experiment/semantic-platform/`

The verdict above evaluated **today's Qyl: 1 producer → 1 consumer**. A second framing was raised:
*future Qyl as a semantic platform* — `1 producer → N consumers` (OTel, Logging, Metrics, Validation,
Audit, OpenAPI, …) composing over a shared, compiler-visible semantic contract. In that graph the
experimental API's value is **composition** (cross-generator type visibility), not AOT. That framing
is legitimate and changes the scope of the verdict, so I built it.

### Built and proven
`experiment/semantic-platform/`: one **producer** (`SemanticContractProducer`) reads a domain
`semantic-contract.tsv` and, via `RegisterPreCompilationSourceOutput`, emits the shared
`[QylSemanticBinding]` attribute + attribute-decorated `partial`s of the user's types into the
**initial** compilation. Two **independent consumers** — `OTelConsumerGenerator`,
`LoggingConsumerGenerator` — **do not reference the producer**; each binds the contract through the
shared compilation in its standard phase and projects it differently:
```
OTel consumer   (keyed by semconv attribute):  activity.SetTag("customer.id", c-1) ...
Logging consumer (keyed by property name):       scope["CustomerId"] = c-1 ...
1 producer (pre-compilation contract) -> 2 consumers, neither referencing the producer.
```
Green build + that output = cross-generator composition works on the merged API.

### Why this genuinely needs #83088 (not post-init)
`RegisterPostInitializationOutput` also lands source in the initial compilation visible to other
generators — but it takes **no inputs**, so it cannot read `semantic-contract.tsv`. The contract here
is **data-driven from an additional file**, which is exactly `RegisterPreCompilationSourceOutput`'s
narrow sweet spot (the design doc's "Configurable Post-Initialization", #53632). So the composition
demo is a real, non-trivial use of #83088.

### The wall, stated precisely (and confirmed empirically)
The compelling version of the vision is **DTO inference → shared contract → N consumers**. It does
**not** work on the merged API, because the pre-compilation phase may not read the compilation/syntax
(`InvalidOperationException`) — so it cannot inspect DTOs. Publishing a **DTO-derived** contract
cross-generator in one compile is the job of the *other* proposal, `RegisterDeclarationOutput`
(#81395), which **is not merged**: grepping the nightly `Microsoft.CodeAnalysis.dll` gives
`RegisterDeclarationOutput` count **0** vs `RegisterPreCompilationSourceOutput` count **1**. The
platform diagram ("Semantic Model Enrichment → Compilation → Many Generators") is #81395's model.

### Three composition patterns, by what is buildable today
| Pattern | Shared contract derived from | Cross-gen visibility via | Today |
|---|---|---|---|
| **A** | user-authored `[QylSemanticBinding]` annotations (a codefix can apply the inference result) | already in user code — every generator sees it | ✅ no experimental API |
| **B** (built here) | additional-file / config | `RegisterPreCompilationSourceOutput` (#83088) | ✅ nightly compiler |
| **C** (the literal vision) | **DTO inference** | `RegisterDeclarationOutput` (#81395) | ❌ unmerged (count 0) |

### Revised verdict, scoped to the platform framing
- The **composition value is real and demonstrated** — for contracts derived from additional files /
  config (Pattern B). For that class, `RegisterPreCompilationSourceOutput` is the right tool, and a
  *hypothetical* multi-consumer Qyl is where it would earn its place. This reframing makes the
  experiment **interesting rather than pointless** — but not *shippable*: the API is still
  unshipped/main-only (`RSEXPERIMENTAL007`), so "rehabilitated" means "has a real use case under a
  future architecture", not "ready to adopt". The single-consumer DREAMING verdict is unchanged for
  today's Qyl.

> **Known limitations of the demo (scope, not hidden bugs, surfaced by adversarial review):** the
> producer addresses **top-level** types only (a dotted FQN cannot encode nesting); the two consumers
> subscribe to `CompilationProvider` directly — the same re-run-per-edit pattern the repo's own
> `SemConvRegistryGenerator` uses, fine for a demo but not for scale; emitted `value.<Property>` access
> assumes public, existing properties.
- The **standard-phase DTO inference is a real, shippable generator** (proven in
  `experiment/contract-precompilation/`: live DTO inspection → bindings, no experimental API).
- But the **headline vision — DTO inference feeding N generators in one compile — is blocked on an
  unmerged API (#81395), not unlocked by #83088.** Today that vision is reachable only via Pattern A
  (materialize the inferred contract as user-visible annotations; the inference generator becomes an
  analyzer + codefix), which needs no experimental API at all.

Net: the original `1→1` DREAMING verdict stands for *today's* Qyl. The `1→N` reframe is sound and the
composition is demonstrated — but the specific, most-exciting capability (compile-time DTO inference
shared across many generators) waits on `RegisterDeclarationOutput`, while everything achievable today
is achievable **without** the experimental pre-compilation API (Pattern A) or with it only for
additional-file-derived contracts (Pattern B).

---

## Addendum: in-place ablation (production generator) — `experiment/precompilation-contract`

Both addenda above measured **isolated** generators (`experiment/contract-precompilation/`,
`experiment/semantic-platform/`). The first pass called the in-place rewrite an "argued projection"
and explicitly scoped it out. This addendum did it **in place, in the shipping build graph**: the
production `QylAutoInstrumentationGenerator` interceptor gate was destructively rewritten to a real
`RegisterPreCompilationSourceOutput` call — the pre-compilation phase emits the implemented-signal
key set as the compilation-native `…Generated.QylContractRegistry` symbol, the standard phase binds it
via `GetTypeByMetadataName`, and `EmitInterceptors` gates on the bound set. The three in-process
lookups (`TryGetImplementedSignal` / `TryGetSourceInterceptorSignal` / `TryGetSupportedSignal` + the
`TryGetSignal` helper) were **deleted** from `InstrumentationContract.cs`.

Each layer was forced by a real build; the error codes are the evidence:

| # | Action | Build result (verbatim code) | What it proves |
|---|---|---|---|
| 1 | Production generator references the API at the shipping toolchain (CPM-pinned `Microsoft.CodeAnalysis.CSharp` 5.3.0, latest stable) | **`error CS1061`**: `IncrementalGeneratorInitializationContext` has no `RegisterPreCompilationSourceOutput` | The API is absent from the shipping toolchain — the generator won't compile. |
| 2 | Bump CSharp → nightly `5.9.0-1.26324.7` (dnceng feed + NU1507 source mapping) | **`error NU1605`** downgrade: nightly CSharp needs `Microsoft.CodeAnalysis.Analyzers >= 5.9.0-1.26319.6` vs CPM's stable `5.3.0` | One nightly pull drags the **whole nightly Roslyn graph** (had to bump Analyzers + Common too). |
| 3 | Generator compiles on nightly; build a real consumer (core runtime) with the repo's **own** SDK (csc `5.6.0.0`) | **`error CS9057`**: analyzer references compiler `5.9.0.0`, newer than running `5.6.0.0` → analyzer **refused to load** | A hard compiler refusal, not a subtle runtime crash. Every consumer on a compiler `< 5.9.0` — i.e. every shipping SDK — gets **zero interceptors**. |
| 4 | Force `Microsoft.Net.Compilers.Toolset 5.9.0` (nightly) on every project via `Directory.Build.props` | **`Build succeeded`** | The only green path forces a nightly main-only compiler onto the *entire* build — and would force it onto every consumer. |
| F | Functional check: AdoNet demo under nightly, `EmitCompilerGeneratedFiles` | `QylContractRegistry.g.cs` emitted (33 keys bound) + `QylAutoInstrumentation.Interceptors.g.cs` gated/emitted | The pre-comp round-trip is **functionally correct on the nightly** — it works, it just can't ship. |

**In-place verdict: DREAMING — confirmed, and sharper.** Non-shippability is no longer an argument; it
is a hard **`CS9057`** refusal on the repo's own SDK. The rewrite is functionally correct *only* when a
nightly, main-only, `[RSEXPERIMENTAL007]`-gated compiler is forced onto the whole build graph (and onto
every consumer). The deleted in-process gate bought nothing: the pre-compilation `QylContractRegistry`
symbol is **emitted from the same baked `InstrumentationContract.ImplementedSignalKeys` data it
replaces** — a pure emit→compile→bind-back round-trip over data already present, for a contract with a
single internal consumer. The original DREAMING trigger, now a compiler error rather than a projection.

Floor breaks are left **red on purpose** as the evidence (this branch is the demonstration, not a
shippable state): `verify-contract-invariants.py` asserts the deleted `TryGet*` methods exist, and the
source-generator snapshot now carries the new `QylContractRegistry.g.cs`. The shippable path is `main`
(untouched). The separable, genuinely-worth-building win is unchanged: deterministic **standard-phase**
compile-time semantic-convention inference over user DTOs/routes — an ordinary `IIncrementalGenerator`
feature (Pattern A above), **not** behind the experimental pre-compilation API.
