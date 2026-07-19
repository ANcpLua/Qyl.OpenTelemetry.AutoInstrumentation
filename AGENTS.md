# Qyl.OpenTelemetry.AutoInstrumentation engineering contract

This is the repository's only editable agent/contributor instruction file.
`CLAUDE.md` is a symlink to it. `README.md` is the public package front door,
`CHANGELOG.md` records released history, and `docs/coverage-matrix.md` is generated
evidence. Do not add progress logs, continuation plans, branch archaeology, or a
second rules file.

## Purpose and boundary

This package family provides managed .NET automatic instrumentation using Roslyn
source generation and interceptors, build assets, BCL `ActivitySource`/`Meter`,
public diagnostic hooks, and module-initializer activation. It does not use a CLR
profiler, startup hook, ReJIT, runtime IL rewriting, dynamic plugin loading, or
reflection-based instrumentation dispatch.

The package family is public. Existing NuGet artifacts are immutable. Make
intentional breaking convergence in a new major version, migrate known consumers,
and do not add compatibility shims without a proven external requirement.

Three API categories are explicit, and 6.0.0 fixed their concrete form. Preserve it:

1. A small supported user API for bootstrap and configuration: Hosting
   `Boot()`/`AddQylAutoInstrumentation(...)`, `Qyl.Sdk` `AddQyl(...)`/`QylSdkOptions`,
   core `AddQylAspNetCoreInstrumentation()`, and the DiagnosticListeners subscriber
   surface with `QylAutoInstrumentationSignal`.
2. A generated-code ABI for cross-assembly interceptor calls, living in the
   `Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode` namespace, every member
   `[EditorBrowsable(EditorBrowsableState.Never)]`, anchored by the
   `QylGeneratedCodeAbi.V8` const that every generated interceptor file references so
   a generator/runtime ABI mismatch fails compilation. That namespace, the anchor, and
   the `V<major>` bump on a breaking ABI change are load-bearing: the snapshot and
   invariant verifiers pin these exact tokens. Do not rename or re-derive them.
   Generated code must not reference `QylAutoInstrumentationOptions` or
   `QylInstrumentationDomains` — gate opt-ins at the policy type and emit domain names
   as literals.
3. Internal implementation types, semantic helpers, listeners, and runtime state —
   everything else, including `QylSemanticAttributes`, `QylActivityNames`,
   `QylActivitySource`, `QylAutoInstrumentationOptions`, and `QylInstrumentationDomains`.
   Reach across assemblies with IVT, never by widening a type to public.

Cross-assembly accessibility does not make generator ABI a user-facing product API.
Any Qyl-specific client-visible request, response, event, or error contract belongs
in `qyl-api-schema`, not in this instrumentation repository.

## Interceptor architecture

The repository uses .NET SDK `10.0.302` with `latestFeature`. Roslyn interceptors are
supported on this SDK. Use `SemanticModel.GetInterceptableLocation(...)` and ordinary
generated C#. The `global.json` pin is a floor: `latestFeature` rolls forward to the
newest installed feature-band patch, so keep the pin, this sentence, and the README
in step when bumping.

Two generated namespaces exist, four characters apart. Do not conflate them:

- `Qyl.OpenTelemetry.AutoInstrumentation.Generated` — where the generator emits
  interceptor methods, and the value `buildTransitive` adds to
  `InterceptorsNamespaces`. Compiler-facing wiring; 6.0.0 did not move it.
- `Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode` — the runtime ABI helpers
  (`QylIntercepted*`, `QylMetricMeters`, the `QylGeneratedCodeAbi.V8` anchor) that
  emitted interceptors call into.

Emitted code lives in the first and delegates to the second. Renaming either side —
or "fixing" the near-duplicate names — breaks the build assets or the pinned
verifier tokens.

Authoritative references:

- Roslyn feature contract: https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md
- ASP.NET Request Delegate Generator: https://github.com/dotnet/aspnetcore/tree/main/src/Http/Http.Extensions/gen/Microsoft.AspNetCore.Http.RequestDelegateGenerator
- Configuration Binding Generator: https://github.com/dotnet/runtime/tree/main/src/libraries/Microsoft.Extensions.Configuration.Binder/gen
- EF Core precompiled-query interceptors: https://github.com/dotnet/efcore/tree/main/src/EFCore.Design/Query/Internal

Prefer a first-class runtime/DI hook when it already owns the behavior. Intercept a
source-visible call only when that is the required substrate; two generators cannot
own the same call site.

## Evidence and generated ownership

- A capability needs an executable owner: a product call path, an owned consumer, or
  a conformance application exercising the complete contract.
- Source-generator snapshots prove generated source shape. Runtime and protocol
  claims require real execution and structural assertions over emitted telemetry.
- Do not use hand-shaped OTLP JSON, fabricated identifiers/timestamps, substring
  searches over protobuf bytes, or mocks that echo inputs as interoperability proof.
  Use official OTLP protobuf types and a real loopback receiver.
- The YAML ownership contract and `tools/generate-contract-artifacts.py` own the
  generated coverage matrix and conformance artifacts. Change inputs/generators,
  regenerate, and commit the outputs together.
- The coverage matrix distinguishes runtime evidence from configuration bindings and
  unsupported rows. Never summarize all 60 contract rows as 60 runtime-implemented or
  NativeAOT-verified integrations.
- Missing runtime values stay missing. Keep span names and metric dimensions bounded;
  sensitive values follow the repository's explicit redaction/opt-in controls.

## Upstream currency

This repository instruments a live ecosystem, not the one training data remembers.
Plans, feasibility verdicts, and roadmaps rot silently; treat every stored claim
about an external library as dated the moment it is written.

- Before judging, planning, or implementing any external library or framework
  integration, verify against live upstream — the package registry and the
  project's own repository — that the target is current, maintained, and has no
  successor. Ask the successor question explicitly; a package that still resolves
  on NuGet can already be legacy. (Canonical failure: Semantic Kernel was judged
  as an integration target after it had merged into Microsoft Agent Framework.)
- Record in the plan or verdict what was checked and on which date. An undated
  feasibility claim about an external library is an opinion, not a finding.
- Subagent and workflow prompts that evaluate external libraries must carry this
  check, and adversarial refuters must include a "is this superseded, deprecated,
  or renamed upstream?" lens.
- Correct drift in what already ships — registered source/meter names, pinned
  upstream identifiers, documented library claims — before adding new integration
  targets. Reconciling the existing surface with upstream reality outranks new
  scope.
- When comparing against the `qyl-references/` clones, pull them first; a stale
  reference clone reintroduces exactly the drift this section exists to prevent.

## Package boundaries

Six projects pack and publish — core, Hosting, DiagnosticListeners, `Qyl.Sdk`,
EntityFrameworkCore, SqlClient — and that set is owned by `.github/workflows/nuget-publish.yml`.

- Core contains shared runtime and compiler-facing ABI only.
- Hosting contains generic bootstrap/DI activation.
- DiagnosticListeners contains public diagnostic-payload consumption.
- `Qyl.Sdk` is the opinionated one-call onboarding surface (`AddQyl(...)`/`QylSdkOptions`)
  layered over Hosting's `Boot()`, plus qyl-specific export concerns: collector
  discovery and session span enrichment. It defines no interceptors.
- EntityFrameworkCore and SqlClient isolate their dependency-heavy integrations.
- SourceGenerators runs inside compilation and remains non-packable as a standalone
  user package.

Do not retain an extra packable project with no published artifact or executable
consumer. Merge a proven extension into its owning package or delete it.

## Verification

Run focused verifiers while iterating. The complete local handoff gate is:

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py
```

Read a gate's own exit code. Piping it through `tail`, `head`, or `tee` reports the
pipe's status, not the gate's, and a masked failure has already reached `main` once.
Redirect to a file and check `$?`, or set `pipefail`. A gate result you did not see
in full is not a green gate.

Verifier tools ship synthetic consumers. Those model *external* consumers, so they
must compile against the public surface alone; when a type moves to internal, fix the
consumer rather than widening the type. A prober that genuinely needs internals gets
a narrowly named IVT, not a public API.

Public API changes update the analyzer-managed shipped/unshipped baselines. Release
work additionally packs the packages, restores them into a clean consumer, executes
managed and NativeAOT smoke tests, publishes through CI, waits for NuGet indexing,
and reruns the consumer smoke against the indexed packages.

## Publishing

NuGet publication is GitHub Actions OIDC trusted publishing through
`.github/workflows/nuget-publish.yml`. Never add a long-lived NuGet API-key secret or
publish locally. The workflow must verify before push, use the repository's version
owner, wait for registry availability, smoke the published artifacts, and only then
create the final tag/release.
