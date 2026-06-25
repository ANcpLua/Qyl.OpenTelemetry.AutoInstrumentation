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
