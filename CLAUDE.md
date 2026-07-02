# Qyl.OpenTelemetry.AutoInstrumentation agent rules

## Mission

This repository is the runtime AOT auto-instrumentation lane for qyl, evolving into a
**self-describing observability substrate**. The foundation is unchanged: .NET 10
NativeAOT-compatible zero-code instrumentation through managed build assets, source generation,
DiagnosticListener consumption, and module-initializer boot. The direction is the North Star
below.

Keep this repository separate from:

- semantic-convention package generation (`Qyl.OpenTelemetry.SemanticConventions` is a *referenced*
  vocabulary package, not generated here),
- the old CLR-profiler/OpenTelemetry auto-instrumentation substrate.

## North Star — declare and prove the whole stack

Every observability tool today is **pull-by-observation**: a backend learns what a service emits
by receiving samples over time, and never knows whether it has seen the whole surface. qyl has a
capability none of them have — because instrumentation is source-generated interceptors + a static
contract + a referenced semconv registry + (incrementally) DTO inference, **the complete set of
telemetry a binary can ever produce is a compile-time-derivable fact, with provenance.**

The substrate goal: every qyl binary ships a complete, machine-readable **Telemetry Capability
Graph (TCG)** — the full possible OpenTelemetry surface for that exact binary, each capability
tagged compile-time-owned vs runtime-valued — and *proves* it by self-hosting (instrumenting its
own pipeline with its own mechanism, zero extra code). Any external entity consumes the TCG to know
the entire stack before a span is sampled. The contract becomes the shared semantic graph; an OTLP
backend is just one consumer.

Three pillars:

1. **Self-host (the proof).** qyl instruments qyl with qyl — `QylSelfTelemetry` /
   `SemConvConformanceProcessor` are the seed; the binary observing itself is how "declared TCG ==
   runtime reality" is checked.
2. **Compile-time-complete TCG (the artifact).** A generated, deterministic manifest of every span
   name / metric / attribute key the binary can produce, with provenance. Only compile-time
   instrumentation can enumerate this honestly.
3. **Open exchange (the reach).** The TCG published vendor-neutrally (OTel Resource/Scope + a static
   artifact + a queryable surface) so a backend can pre-provision, a collector can validate against
   the declared surface, and CI can treat telemetry as a typed, versioned API.

**Status (current tree — do not overstate):** First-Light steps 1–3 are shipped —
`TelemetryCapabilityGraphGenerator` bakes the TCG into the core assembly's public type
`QylTelemetryCapabilityGraph` (its manifest body filled via a generator `partial`, gated to the core
assembly like `SemConvRegistryGenerator`) — `.Json` / `.SchemaVersion` / `.CapabilityCount` (the
queryable surface), with the vendor-neutral exchange schema in
`docs/schema/telemetry-capability-graph.schema.json` and `docs/TELEMETRY_CAPABILITY_GRAPH.md`. The
`Qyl.OpenTelemetry.AutoInstrumentation.Publishing` package adds the runtime open-exchange channel:
`AddQylTelemetryCapabilityGraphPublisher()` emits the TCG as a true OTel `LogRecord` at host startup
through `ILogger` (the OTLP exporter stays app-owned; no OTel SDK dependency in qyl), proven by
`demos/Qyl.RealTcgPublishingDemo`. Next: the static build-artifact channel and a remote queryable endpoint.

**What does NOT change:** runtime DiagnosticListeners stay. ~95% of attribute *values* are
runtime-only — listeners are the **runtime lane** of the TCG, not a failure to delete. Semantic ownership trends toward compile time; runtime owns
observations. "Missing values stay missing; never synthesize" still governs every lane, including
any future inference.

## Clean slate before work

Before implementation work, confirm:

```bash
git worktree list
git branch --show-current
git diff --cached --name-only
git stash list
git status --short
```

Work from `main` unless the task explicitly asks for a topic branch, and hand the tree back as
clean as you found it — no stale local branches, stashes, staged files, or unrelated untracked
files left behind.

## Build and test reality

- SDK is pinned by `global.json` (10.0.300, `rollForward: latestFeature`).
- Build everything: `dotnet build Qyl.OpenTelemetry.AutoInstrumentation.slnx`.
- `TreatWarningsAsErrors` is on repo-wide with a heavy analyzer stack (trim/AOT/single-file
  analyzers, ErrorProne.NET, Roslynator, PublicApiAnalyzers on packaged projects). A clean
  build is the validation floor; analyzer regressions fail the build by design.
- There are no `dotnet test` projects. Behavior is proven by the Python verifiers in `tools/`
  and the snapshot fixture under `tests/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.Snapshots`
  (compare against `verified/`). Route changes through the validation table below.
- Public API changes require updating the `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`
  baselines next to each packaged project (`python3 tools/verify-public-api-baseline.py`).
- CI runs `tools/smoketest.sh` on pull requests and pushes to `main`, plus the OTLP collector
  fixture and WebAPI AOT demo workflows under `.github/workflows/`.
- CI runs entirely on **GitHub-hosted runners** (the repo is public, so they are free):
  `ubuntu-24.04-arm` and `macos-latest` — both ARM64 to match the NativeAOT target matrix. On
  pull requests and pushes to `main`: the smoketest, the `verify` validation floor, the OTLP
  collector fixtures, and the WebAPI AOT demo; post-merge/nightly: the AOT-publish gate and the
  container-backed real-demo verifiers (Docker is preinstalled on the hosted Ubuntu images).
  Linux legs install the NativeAOT prerequisites (`clang`, `zlib1g-dev`); the whole gate is
  `tools/verify-aot-autoinstrumentation-goal.py`. There are no self-hosted runners — nothing to
  start, babysit, or avoid colliding with locally.

## Mechanism flow (big picture)

1. Package `build/`/`buildTransitive/` assets enable the `Qyl.OpenTelemetry.AutoInstrumentation.Generated`
   interceptor namespace in the consumer and inject a local `InterceptsLocationAttribute`
   source file.
2. `Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators` (netstandard2.0, runs inside the compiler)
   discovers supported source-visible call-sites and emits ordinary C# `[InterceptsLocation]`
   interceptors plus generated semantic registries. The descriptor model is structural: a matcher
   row is `(Name, ReceiverTypePattern, TryMatch)`, an emission row is `(Kind, Body)` where the body
   is one sealed record of the closed `InterceptorBodyDescriptor` hierarchy and the emitter
   dispatches on its concrete type. Catalog invariants (kind uniqueness/completeness, contract-key
   coverage) are enforced by `tools/verify-contract-invariants.py`, not by runtime validation in
   `Initialize()` — do not reintroduce declaration metadata that exists only to be validated.
3. `[ModuleInitializer]` bootstrap in the Hosting/EFCore/SqlClient packages activates qyl once
   per process — no app code changes.
4. Runtime listeners (`Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners`) consume framework/library
   DiagnosticListener payloads. Missing values stay missing; never synthesize.
5. Output uses stable OpenTelemetry attributes with bounded values (route templates over raw
   paths, no raw text in span names — see `docs/RUNTIME_SEMANTICS.md`). Query-string values are
   redacted by default (upstream `OTEL_DOTNET_EXPERIMENTAL_*_DISABLE_URL_QUERY_REDACTION`
   flags switch redacted to raw); `db.query.text` sits behind the upstream
   `SET_DBSTATEMENT_FOR_TEXT` flags; the conformance processor behind
   `QYL_CONFORMANCE_ENABLED=1`.
6. The **Telemetry Capability Graph** (`TelemetryCapabilityGraphGenerator`) bakes the instrumentation
   contract into the core assembly as `QylTelemetryCapabilityGraph.Json` — the binary's declared,
   provenance-tagged telemetry surface (North Star pillar 2). Generated from the same contract data,
   so it is a pure function of its inputs and never drifts from what the lanes above actually emit.
7. The ASP.NET Core server span is middleware registered via `IStartupFilter`
   (`AddQylAspNetCoreInstrumentation()`); per-signal double-emission across the interceptor,
   middleware, and DiagnosticListener lanes is arbitrated at runtime by the single-owner
   `QylSignalOwnership` registry — exactly one owner per signal per process.

Key gotcha: a bare `ProjectReference` to the runtime project does NOT flow analyzer/build
assets — `PackageReference` is the supported zero-code path. The dogfood path references the
runtime project, the generator project as analyzer, and the core targets file explicitly
(proven by `tools/verify-projectreference-behavior.py`).

## Architecture invariants

The accepted qyl instrumentation mechanisms here are exactly these: ordinary C# compiled into
the app, source-generated interceptors, build-transitive assets, module-initializer activation,
BCL telemetry primitives, and public library diagnostic payloads. Build every integration — and
the entire self-describing surface (the TCG, self-host telemetry, any future inference) — out of
these and nothing else.

Interceptors are the right tool only where no runtime seam exists. Where ASP.NET Core (or any
host) offers a first-class seam (`IStartupFilter`, DI), use the seam — an intercepted call site is
an exclusive resource and two generators wanting it is a build break (CS9153). Do not reintroduce
a `Build()`/`CreateBuilder()` interceptor or any cross-generator coordination protocol
(MSBuild opt-out knobs, reference sniffing, compose wrappers) to arbitrate call-site ownership.

The following are the old substrate this repo deliberately replaced; reintroducing any of them
into product code or package assets is out of bounds:

- CLR profiler attach,
- startup hooks,
- runtime IL rewriting,
- ReJIT,
- `AssemblyLoadContext` plugin loading,
- `qyl install` style substrate deployment,
- `gate.sh` substrate attach flows,
- reflection-based instrumentation dispatch.

## Package boundaries

Keep dependency-heavy integrations isolated:

- EFCore code belongs in `Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore`.
- Microsoft.Data.SqlClient code belongs in `Qyl.OpenTelemetry.AutoInstrumentation.SqlClient`.
- Generic hosting/bootstrap code belongs in `Qyl.OpenTelemetry.AutoInstrumentation.Hosting`.
- Core shared runtime helpers belong in `Qyl.OpenTelemetry.AutoInstrumentation`.
- TCG runtime publishing (the OTel-`LogRecord` exchange channel) belongs in
  `Qyl.OpenTelemetry.AutoInstrumentation.Publishing` — opt-in, `ILogger`-based, no OTel SDK dependency.

EFCore lives in `Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore` and SqlClient in
`Qyl.OpenTelemetry.AutoInstrumentation.SqlClient`; their dependencies, build warnings, and app-side NativeAOT
constraints stay contained there. Hosting and the core runtime package keep a clean,
dependency-light surface — leaking any EFCore or SqlClient dependency, warning, or NativeAOT
constraint into them is out of bounds.

## Generated and evidence files

Generated output's single source of truth is its generator and inputs: change one of those,
then regenerate. Hand-editing generated output is out of bounds — such edits don't survive the
next regeneration.

Generated/evidence surfaces include:

- EFCore compiled models under `demos/Qyl.RealEfCoreDemo/CompiledModels`,
- source-generator verified snapshots,
- OTLP/verified fixture files,
- generated coverage matrix,
- the Telemetry Capability Graph (`QylTelemetryCapabilityGraph.g.cs`, from the instrumentation
  contract via `TelemetryCapabilityGraphGenerator`),
- package build/buildTransitive generated assets.

If a generated file is obsolete, delete it only with the generator/input change that makes it
obsolete.

## Documentation rules

- Document current-tree behavior, not old PR state or ceremonial progress claims.
- The North Star is direction, not a claim of completion — mark what is shipped vs next, and never
  describe a future pillar as if it already exists.
- Do not claim a tag or GitHub Release is current without checking its target commit.
- Keep README user-facing and operational.
- Keep CHANGELOG synthetic and useful for continuation, not a raw commit dump.
- `CLAUDE.md` (this file) is the single agent-rules file for this repository.

## Validation routing

Use the narrowest verifier that covers the changed surface:

| Changed surface | Command |
|---|---|
| Package build assets | `python3 tools/verify-package-layout.py` |
| ProjectReference behavior | `python3 tools/verify-projectreference-behavior.py` |
| Source generator snapshots | `python3 tools/verify-generator-snapshots.py` |
| Source interceptor behavior | `python3 tools/verify-source-interceptor-consumer.py` |
| Contract/catalog invariants | `python3 tools/verify-contract-invariants.py` |
| Telemetry Capability Graph | `dotnet build src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj` (emits `QylTelemetryCapabilityGraph.g.cs`) |
| NativeAOT smoke | `bash tools/smoketest.sh` |
| OTLP fixtures | `python3 tools/verify-otlp-fixtures.py` and `python3 tools/verify-otlp-collector-fixtures.py` |
| Whole repo handoff | `python3 tools/verify-aot-autoinstrumentation-goal.py` |

For release/handoff work, run the whole repo handoff gate.

## Commit and release hygiene

For file changes, commit and push the intended scope. If the package version or release marker is
changed, align tags only after validation and after confirming the tag target is the final commit.
Use `--force-with-lease` for intentional history rewrites; never rewrite remote history by accident.

## NuGet publishing (keyless — never an API key)

Publishing is keyless OIDC **Trusted Publishing**, driven by `nuget-publish.yml`
(`workflow_dispatch` only — qyl stays unreleased until deliberately published, and stays on a
GitHub-hosted `ubuntu-latest` runner for reliable OIDC + `gh`). There is **no `NUGET_API_KEY`
secret and you must never add one**: `NuGet/login` exchanges GitHub's OIDC token for a one-hour,
single-use key at push time (requires `permissions: id-token: write` + `environment: nuget`).

If the publish job fails at **"Authenticate to NuGet"**, the cause is a **missing nuget.org
Trusted Publishing policy — never a missing key**. That policy is the one human-only step (no
API for it); create it once at nuget.org → Trusted Publishing → Create with these fields:
Package Owner `ANcpLua`, Repository Owner `ANcpLua`, Repository
`Qyl.OpenTelemetry.AutoInstrumentation` (name only), Workflow File `nuget-publish.yml` (filename
only, no `.github/workflows/` prefix), Environment `nuget` (must match the publish job). Then
re-run the failed job: `gh run rerun <run-id> --failed`. Never park publishing as "blocked on
credentials." Versioning has two roles: the **publish** version comes from the workflow (explicit
input, else auto patch-bump from the latest reachable `v*` tag), while the **build** version's
single source of truth is `Directory.Build.props` `<Version>` — gated against drift by the
version-sync check in `tools/verify-contract-invariants.py`. Keep the two aligned at release time.
