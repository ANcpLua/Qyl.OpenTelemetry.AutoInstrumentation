# qyl-dotnet-autoinstrumentation agent rules

## Mission

This repository is the runtime AOT auto-instrumentation lane for qyl. Keep it separate from:

- semantic-convention package generation,
- the old CLR-profiler/OpenTelemetry auto-instrumentation substrate,
- unrelated compile-time tracing experiments.

The product goal is .NET 10 NativeAOT-compatible zero-code instrumentation through managed
build assets, source generation, DiagnosticListener consumption, and module-initializer boot.

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
- Build everything: `dotnet build Qyl.AutoInstrumentation.slnx`.
- `TreatWarningsAsErrors` is on repo-wide with a heavy analyzer stack (trim/AOT/single-file
  analyzers, ErrorProne.NET, Roslynator, PublicApiAnalyzers on packaged projects). A clean
  build is the validation floor; analyzer regressions fail the build by design.
- There are no `dotnet test` projects. Behavior is proven by the Python verifiers in `tools/`
  and the snapshot fixture under `tests/Qyl.AutoInstrumentation.SourceGenerators.Snapshots`
  (compare against `verified/`). Route changes through the validation table below.
- Public API changes require updating the `PublicAPI.Shipped.txt`/`PublicAPI.Unshipped.txt`
  baselines next to each packaged project (`python3 tools/verify-public-api-baseline.py`).
- CI runs `tools/smoketest.sh` on every push/PR, plus the OTLP collector fixture and WebAPI
  AOT demo workflows under `.github/workflows/`.
- **CI runs on the two self-hosted arm64 runners** — `qyl-linux` (in the OrbStack machine
  `qyl-ci`) and `qyl-macos` (on the dev Mac). These are the accepted CI substrate: the repo
  stays private and GitHub-hosted minutes are exhausted, so the self-hosted pair is what every
  workflow targets. Out of bounds: switching any workflow back to `ubuntu-latest`/`macos-latest`,
  and making the repo public to unblock CI — it stays private until the deliberate history
  overhaul. Operations, recreation, and troubleshooting:
  `.claude/skills/qyl-selfhosted-ci/SKILL.md` (the `qyl-selfhosted-ci` skill).

## Mechanism flow (big picture)

1. Package `build/`/`buildTransitive/` assets enable the `Qyl.AutoInstrumentation.Generated`
   interceptor namespace in the consumer and inject a local `InterceptsLocationAttribute`
   source file.
2. `Qyl.AutoInstrumentation.SourceGenerators` (netstandard2.0, runs inside the compiler)
   discovers supported source-visible call-sites and emits ordinary C# `[InterceptsLocation]`
   interceptors plus generated semantic registries.
3. `[ModuleInitializer]` bootstrap in the Hosting/EFCore/SqlClient packages activates qyl once
   per process — no app code changes.
4. Runtime listeners (`Qyl.AutoInstrumentation.DiagnosticListeners`) consume framework/library
   DiagnosticListener payloads. Missing values stay missing; never synthesize.
5. Output uses stable OpenTelemetry attributes with bounded values (route templates over raw
   paths, no raw text in span names — see `docs/RUNTIME_SEMANTICS.md`). Query-string values are
   redacted by default (upstream `OTEL_DOTNET_EXPERIMENTAL_*_DISABLE_URL_QUERY_REDACTION`
   flags switch redacted to raw); `db.query.text` sits behind the upstream
   `SET_DBSTATEMENT_FOR_TEXT` flags; the conformance processor behind
   `QYL_CONFORMANCE_ENABLED=1`.

Key gotcha: a bare `ProjectReference` to the runtime project does NOT flow analyzer/build
assets — `PackageReference` is the supported zero-code path. The dogfood path references the
runtime project, the generator project as analyzer, and the core targets file explicitly
(proven by `tools/verify-projectreference-behavior.py`).

## Architecture invariants

The accepted qyl instrumentation mechanisms here are exactly these: ordinary C# compiled into
the app, source-generated interceptors, build-transitive assets, module-initializer activation,
BCL telemetry primitives, and public library diagnostic payloads. Build every integration out
of these.

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

- EFCore code belongs in `Qyl.AutoInstrumentation.EntityFrameworkCore`.
- Microsoft.Data.SqlClient code belongs in `Qyl.AutoInstrumentation.SqlClient`.
- Generic hosting/bootstrap code belongs in `Qyl.AutoInstrumentation.Hosting`.
- Core shared runtime helpers belong in `Qyl.AutoInstrumentation`.

EFCore lives in `Qyl.AutoInstrumentation.EntityFrameworkCore` and SqlClient in
`Qyl.AutoInstrumentation.SqlClient`; their dependencies, build warnings, and app-side NativeAOT
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
- package build/buildTransitive generated assets.

If a generated file is obsolete, delete it only with the generator/input change that makes it
obsolete.

## Documentation rules

- Document current-tree behavior, not old PR state or ceremonial progress claims.
- Do not claim a tag or GitHub Release is current without checking its target commit.
- Keep README user-facing and operational.
- Keep CHANGELOG synthetic and useful for continuation, not a raw commit dump.
- Keep `CLAUDE.md` as a symlink to this file; edit `AGENTS.md` only.

## Validation routing

Use the narrowest verifier that covers the changed surface:

| Changed surface | Command |
|---|---|
| Package build assets | `python3 tools/verify-package-layout.py` |
| ProjectReference behavior | `python3 tools/verify-projectreference-behavior.py` |
| Source generator snapshots | `python3 tools/verify-generator-snapshots.py` |
| Source interceptor behavior | `python3 tools/verify-source-interceptor-consumer.py` |
| NativeAOT smoke | `bash tools/smoketest.sh` |
| OTLP fixtures | `python3 tools/verify-otlp-fixtures.py` and `python3 tools/verify-otlp-collector-fixtures.py` |
| Whole repo handoff | `python3 tools/verify-aot-autoinstrumentation-goal.py` |

For release/handoff work, run the whole repo handoff gate.

## Commit and release hygiene

For file changes, commit and push the intended scope. If the package version or release marker is
changed, align tags only after validation and after confirming the tag target is the final commit.
Use `--force-with-lease` for intentional history rewrites; never rewrite remote history by accident.
