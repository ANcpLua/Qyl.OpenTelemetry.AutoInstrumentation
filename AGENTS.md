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

Work from `main` unless the task explicitly asks for a topic branch. Do not leave stale local
branches, stashes, staged files, or unrelated untracked files behind.

## Architecture invariants

Never reintroduce these mechanisms into product code or package assets:

- CLR profiler attach,
- startup hooks,
- runtime IL rewriting,
- ReJIT,
- `AssemblyLoadContext` plugin loading,
- `qyl install` style substrate deployment,
- `gate.sh` substrate attach flows,
- reflection-based instrumentation dispatch.

The only accepted qyl mechanisms here are ordinary C# compiled into the app, source-generated
interceptors, build-transitive assets, module-initializer activation, BCL telemetry primitives,
and public library diagnostic payloads.

## Package boundaries

Keep dependency-heavy integrations isolated:

- EFCore code belongs in `Qyl.AutoInstrumentation.EntityFrameworkCore`.
- Microsoft.Data.SqlClient code belongs in `Qyl.AutoInstrumentation.SqlClient`.
- Generic hosting/bootstrap code belongs in `Qyl.AutoInstrumentation.Hosting`.
- Core shared runtime helpers belong in `Qyl.AutoInstrumentation`.

Do not leak EFCore or SqlClient dependencies, warnings, or app-side NativeAOT constraints into
Hosting or the core runtime package.

## Generated and evidence files

Do not hand-edit generated output. Fix the generator or input, then regenerate.

Generated/evidence surfaces include:

- EFCore compiled models under `demos/Qyl.RealEfCoreDemo/CompiledModels`,
- source-generator verified snapshots,
- OTLP/golden fixture files,
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
| OTLP fixtures | `python3 tools/verify-otlp-golden-fixtures.py` and `python3 tools/verify-otlp-collector-fixtures.py` |
| Whole repo handoff | `python3 tools/verify-aot-autoinstrumentation-goal.py` |

For release/handoff work, run the whole repo handoff gate.

## Commit and release hygiene

For file changes, commit and push the intended scope. If the package version or release marker is
changed, align tags only after validation and after confirming the tag target is the final commit.
Use `--force-with-lease` for intentional history rewrites; never rewrite remote history by accident.
