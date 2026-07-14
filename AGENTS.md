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

Keep three API categories explicit:

1. A small supported user API for bootstrap and configuration.
2. A generated-code ABI for cross-assembly interceptor calls. Keep it in a clearly
   named namespace, hide it from normal completion, and version it deliberately.
3. Internal implementation types, semantic helpers, listeners, and runtime state.

Cross-assembly accessibility does not make generator ABI a user-facing product API.
Any Qyl-specific client-visible request, response, event, or error contract belongs
in `qyl-api-schema`, not in this instrumentation repository.

## Interceptor architecture

The repository uses .NET SDK `10.0.301` with `latestFeature`. Roslyn interceptors are
supported on this SDK. Use `SemanticModel.GetInterceptableLocation(...)` and ordinary
generated C#.

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

## Package boundaries

- Core contains shared runtime and compiler-facing ABI only.
- Hosting contains generic bootstrap/DI activation.
- DiagnosticListeners contains public diagnostic-payload consumption.
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
