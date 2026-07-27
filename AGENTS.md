# Qyl.Telemetry.AutoInstrumentation engineering contract

This is the repository's only editable agent/contributor instruction file.
`CLAUDE.md` is a symlink to it. `README.md` is the public package front door,
`CHANGELOG.md` records released history, and `docs/coverage-matrix.md` is generated
evidence. Do not add progress logs, continuation plans, branch archaeology, or a
second rules file.

## Package identity

This repository ships the **`Qyl.Telemetry.*`** family. The rename has landed;
these are the package IDs, not a target:

| Package | What lives there |
| --- | --- |
| `Qyl.Telemetry` | Primitives and the explicit instrumentation API (not in this repo yet) |
| `Qyl.Telemetry.AutoInstrumentation` (+ `.Hosting`, `.DiagnosticListeners`, `.EntityFrameworkCore`, `.SqlClient`) | Automatic capture |
| `Qyl.Telemetry.Hosting` | Composition: `AddQyl()`, OTel wiring, OTLP export, collector discovery — absorbs the retired `Qyl.Sdk` (8.x) |

The retired IDs — `Qyl.OpenTelemetry.AutoInstrumentation(.Hosting/.DiagnosticListeners/
.EntityFrameworkCore/.SqlClient)` and `Qyl.Sdk` — stay frozen on nuget.org at
`8.5.0` / `QylGeneratedCodeAbi.V8`. They are never republished; they are unlisted
at launch. Nothing in this repository builds them any more.

**The emitted scope name still reads `Qyl.OpenTelemetry.AutoInstrumentation`, on
purpose.** `QylActivitySource.Name` and the two qyl-owned meter names
(`…AutoInstrumentation.Database`, `…AutoInstrumentation.NServiceBus`) are *runtime
vocabulary*, not package identity: they are stamped on every emitted span and
metric, and the collector repo's conformance app asserts the inbound `Source.Name`
literally. Renaming a package ID does not rename what it emits. Moving those three
literals is its own slice, and it only lands together with the consumer-side
assertions, in the publish → NuGet-index → consumer-bump order the workspace router
mandates. Until then this mismatch is the correct state — do not "fix" it because it
looks inconsistent with the namespace around it.

`qyl/internal/qyl.instrumentation(.generators)` folds in from the other repo.
Target state after the move: **`InternalsVisibleTo` count = 0**. The consumer
contract `builder.AddQyl()` does not change.

The full ledger and the boundary law live in `qyl/ARCHITECTURE-1.0.0.md` — that
document is normative and this one does not restate it. `qyl/docs/component-taxonomy.html`
is a human view of it, never a second source.

**Where this family stops.** It composes the producer pipeline inside the
customer's application and **ends at the OTLP exporter**. It never stores,
queries, or validates telemetry. The one testable consequence: `AddQyl()` must
be fully exercisable with **no collector running**. The conformance app's
assertion on the inbound `Source.Name` verifies exactly that, and it is the
signal that the split is real rather than aspirational. If a test here ever
needs a collector package, the boundary has leaked.

## Purpose and boundary

This package family provides managed .NET automatic instrumentation using Roslyn
source generation and interceptors, build assets, BCL `ActivitySource`/`Meter`,
public diagnostic hooks, and module-initializer activation. It does not use a CLR
profiler, startup hook, ReJIT, runtime IL rewriting, dynamic plugin loading, or
reflection-based instrumentation dispatch.

The package family is public. Existing NuGet artifacts are immutable. Make
intentional breaking convergence in a new major version, migrate known consumers,
and do not add compatibility shims without a proven external requirement.

The following API/ABI categories define the active architecture. Preserve them:

1. A small supported user API for bootstrap and configuration: Hosting
   `Boot()`/`AddQylAutoInstrumentation(...)`, `Qyl.Telemetry.Hosting` `AddQyl(...)`/`QylSdkOptions`,
   core `AddQylAspNetCoreInstrumentation()`, and the DiagnosticListeners subscriber
   surface with `QylAutoInstrumentationSignal`.
2. A generated-code ABI for cross-assembly interceptor calls, living in the
   `Qyl.Telemetry.AutoInstrumentation.GeneratedCode` namespace, every member
   `[EditorBrowsable(EditorBrowsableState.Never)]`, anchored by the
   `QylGeneratedCodeAbi.V9` const that every generated interceptor file references so
   a generator/runtime ABI mismatch fails compilation. That namespace, the anchor, and
   the `V<major>` rule are load-bearing: the version-sync and generated-source snapshot
   verifiers pin the exact token. Do not rename or re-derive it — `<major>` is the
   package major, checked mechanically (`verify-version-sync.py` requires the anchor
   declaration to equal it exactly), so the anchor moves when the package major moves
   and never on its own. The frozen `Qyl.OpenTelemetry.*` IDs keep `V8` under their own
   old namespace; `V9` here is a first use, not a reuse, because the fully-qualified
   token changed namespace with the package ID.
   Generated code must not reference `QylAutoInstrumentationOptions` or
   `QylInstrumentationDomains` — gate opt-ins at the policy type and emit domain names
   as literals.
3. A narrow generated-code/build-transitive package bootstrap ABI, also under the
   `Qyl.Telemetry.AutoInstrumentation.GeneratedCode` namespace and hidden with
   `[EditorBrowsable(EditorBrowsableState.Never)]`:
   `EntityFrameworkCoreAutoInstrumentationBootstrap` and
   `SqlClientAutoInstrumentationBootstrap`. They are public because generated source
   compiled into consumer assemblies calls them; they are package plumbing, not user
   configuration APIs.
4. Internal implementation types, semantic helpers, listeners, meter registration
   inventory, and runtime state —
   everything else, including `QylSemanticAttributes`, `QylActivityNames`,
   `QylActivitySource`, `QylAutoInstrumentationOptions`, `QylInstrumentationDomains`,
   and `QylMetricMeters`. Reach across assemblies with IVT, never by widening a type to
   public.

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

- `Qyl.Telemetry.AutoInstrumentation.Generated` — where the generator emits
  interceptor methods, and the value `buildTransitive` adds to
  `InterceptorsNamespaces`. Compiler-facing wiring; it remains load-bearing.
- `Qyl.Telemetry.AutoInstrumentation.GeneratedCode` — the runtime ABI helpers
  (`QylIntercepted*` and the `QylGeneratedCodeAbi.V9` anchor) that emitted
  interceptors call into.

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
  unsupported rows. Never summarize the 60-row upstream contract or the separate
  qyl-native promises as universally runtime-implemented or NativeAOT-verified.
- Missing runtime values stay missing. Keep span names and metric dimensions bounded;
  sensitive values follow the repository's explicit redaction/opt-in controls.

## MCP telemetry and protocol-era discipline

This package emits MCP telemetry through `ActivitySource`/`Meter`, and the MCP
2026-07-28 revision changes what several recorded fields mean. These rules bind what
the instrumentation records.

- Tag protocol era from the negotiated protocol version, never the presence of a
  `_meta` envelope: the legacy-fallback probe also carries one.
- MCP client and server identity is per-request and self-reported. Do not read it
  from a session-scoped accessor, and never promote `clientInfo`/`serverInfo` to a
  resource attribute, a span dimension, or a behavior or security decision — they are
  display, logging, and debugging values only.
- A multi-round tool call is N linked requests correlated by an opaque, untrusted
  `requestState`. Correlate the rounds with `Activity` links, never a synthesized
  parent-child tree, and trust `requestState` only after verification.
- Derive `ActivityStatusCode` and the RPC/error attributes from the JSON-RPC and tool
  outcome, never the HTTP status: on the modern path a well-formed JSON-RPC error
  rides HTTP 400, and an error can arrive in-band on a committed 200.
- Wire concepts OpenTelemetry semconv has not defined — `requestState`, round index,
  `resultType`, `subscriptions/listen` lifetime, cache hints — are recorded under an
  experimental `qyl.mcp.*` staging namespace in `QylSemanticAttributes`,
  deletion-targeted on every semconv bump that lands an upstream equivalent. Never
  mint an `mcp.*` alias for an unratified concept.

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

Six projects pack and publish — core, Hosting, DiagnosticListeners, `Qyl.Telemetry.Hosting`,
EntityFrameworkCore, SqlClient — and that set is owned by `.github/workflows/nuget-publish.yml`.

- Core contains shared runtime and compiler-facing ABI only.
- Hosting contains generic bootstrap/DI activation.
- DiagnosticListeners contains public diagnostic-payload consumption.
- `Qyl.Telemetry.Hosting` (the retired `Qyl.Sdk` under its new identity: PackageId and AssemblyName changed, namespace `Qyl` and `AddQyl()` unchanged) is the opinionated one-call onboarding surface (`AddQyl(...)`/`QylSdkOptions`)
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
create the final release.

Publication triggers on version tags only (qyl architecture §5): pushing the tag
`v<version>` — which must equal `<Version>` in `Directory.Build.props` at that
commit — is the human act that publishes. A push to `main` builds and verifies
through the sibling workflows and never publishes, so a rename or any other change
lands on `main` with zero registry effect until someone tags.

Two invariants survive any edit to the workflow:

- **Idempotence.** The `version` job checks nuget.org and short-circuits when the
  version is already indexed, so a re-pushed or re-run tag costs one lookup rather
  than the full pipeline. A missing package index is the expected first-publish
  state; every other failed lookup fails closed. Published artifacts are immutable;
  never rebuild and re-push one.
- **Green before publish.** `publish` depends on both `verify` and `aot-publish`, so
  the gate is enforced inside this workflow rather than inferred from a sibling run.
  The GitHub release is still created last, after `verify-published`.

`workflow_dispatch` remains, and it bypasses the index check so a run that died after
`verify` but before `publish` can be re-driven.
