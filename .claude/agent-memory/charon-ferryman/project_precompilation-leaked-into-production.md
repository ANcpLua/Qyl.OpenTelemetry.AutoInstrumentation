---
name: precompilation-experiment-leaked-into-production-generators
description: The nightly-Roslyn precompilation experiment (RegisterPreCompilationSourceOutput / RSEXPERIMENTAL007) was wired into the PRODUCTION generators, not kept in the experiment lane
metadata:
  type: project
---

On the `feat/telemetry-capability-graph` branch (PR #12), the experimental
`RegisterPreCompilationSourceOutput` API (gated `RSEXPERIMENTAL007`, exists ONLY in
nightly Roslyn `main` / 5.9.0 — no released SDK ships it) was used directly inside the
**production** `SourceGenerators` project: `QylAutoInstrumentationGenerator.cs`,
`QylSemanticContractProducer.cs`, `InstrumentationContract.cs`. To make that compile, the
production `Directory.Packages.props` Roslyn pins were bumped from stable `5.3.0` to nightly
`5.9.0-1.26324.7` and `Microsoft.Net.Compilers.Toolset 5.9.0` was forced repo-wide via
`Directory.Build.props`.

**Why this matters:** AGENTS.md states the `experiment/` + `spike/` trees must stay OUTSIDE
the `.slnx` / production build graph. Those trees do isolate it correctly (own nuget.config +
Directory.Packages.props on the nightly feed). The leak is that production generators ALSO
depend on the nightly-only API, so the published package's analyzer references compiler 5.9.0
and is refused (`CS9057`) by every released SDK compiler — the zero-code instrumentation
value prop silently breaks for consumers.

**How to apply:** Removing this dependency from production = reversing the design of commits
`6b976f2` (bind implemented-signal gate from pre-compilation QylContractRegistry) and
`1df2214` (semantic-contract producer). That is a scope/design decision for the human, NOT a
mechanical CI fix — especially since the human is actively hand-editing the precompilation
experiment lane (`docs/experiments/precompilation-verdict.md`, `experiment/semantic-platform/`).
The headline TCG feature (`TelemetryCapabilityGraphGenerator`, commits `6881fdb`/`cb72007`)
does NOT use the experimental API and is clean. See [[slnx-green-hides-analyzer-breakage]].
