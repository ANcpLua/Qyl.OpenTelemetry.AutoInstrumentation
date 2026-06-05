# Score 100 Justification

This document is the current evidence ledger for the NativeAOT interceptor score. It is intentionally
command-backed: every claim below points at a committed gate, a CI check, or a reproducible local command.

// validated 2026-06-05 20:19 CEST by tools/verify-aot-autoinstrumentation-goal.py and gh pr checks 1 --watch --interval 10

## Current release candidate

| Item | Value |
|---|---|
| Branch | `claude/projectreference-build-assets` |
| Current evidence SHA | `0fce5ab2286fb56c86974bf733f4550e60629049` |
| Current package version | `0.3.0-pre.1` |
| Release tag target | `v0.3.0-pre.1` |
| Pull request | `https://github.com/ANcpLua/qyl-dotnet-autoinstrumentation/pull/1` |

The release tag is created after the version-bump commit because a commit cannot include its own SHA in a
tracked file. The authoritative release commit SHA is the `v0.3.0-pre.1` tag target reported by Git.

## Local proof commands

The current local release gate completed successfully with:

```text
python3 tools/verify-aot-autoinstrumentation-goal.py
```

Observed terminal result:

```text
contract-invariants-ok
contract-coverage-report-ok total=60 source_generated_signals=33 unsupported_signals=4 environment_controls=7 instrumentation_options=16
Build succeeded.
0 Warning(s)
0 Error(s)
package-layout-ok
rfc-artifact-ok
projectreference-behavior-ok
public-api-baseline-ok
xml-doc-enforcement-ok scope=source-generator,runtime
benchmark-report-ok
environment-options-behavior-ok
conformance-opt-in-ok
generator-snapshots-ok
source-interceptor-consumer-ok
aot-warning-gate-ok consumer=package-reference warnings=0
aot-warning-gate-ok consumer=project-reference warnings=0
smoketest-ok rid=osx-arm64
webapi-aot-demo-ok qyl_warnings=0
otlp-golden-fixtures-ok
otlp-collector-fixtures-ok
consumer-behavior-ok
nativeaot-consumer-golden-ok
aot-autoinstrumentation-goal-ok
```

The final `diff whitespace` step also completed inside the same orchestrator.

## CI proof

`gh pr checks 1 --watch --interval 10` completed with all checks passing for PR #1:

```text
GitGuardian Security Checks pass
aot warnings (macos-latest) pass
aot warnings (ubuntu-latest) pass
otlp collector fixtures (macos-latest) pass
otlp collector fixtures (ubuntu-latest) pass
smoke (macos-latest) pass
smoke (ubuntu-latest) pass
webapi-aot-demo (macos-latest) pass
webapi-aot-demo (ubuntu-latest) pass
```

GitHub reported duplicate push and pull-request runs for several jobs; both duplicate runs completed
successfully.

## Gate map

| Requirement | Evidence gate | Why this proves the requirement |
|---|---|---|
| Source generator uses the AOT interceptor substrate | `tools/verify-contract-invariants.py`; `tools/verify-generator-snapshots.py` | The invariant gate checks for Roslyn interceptable-location APIs and the snapshot gate commits exact generated `[InterceptsLocation]` output. |
| PackageReference and ProjectReference both receive interception assets | `tools/verify-package-layout.py`; `tools/verify-projectreference-behavior.py`; `tools/smoketest.sh` | Package layout checks both `build/` and `buildTransitive/`; ProjectReference behavior executes a generated-interceptor consumer; smoke runs both package modes under JIT and NativeAOT. |
| NativeAOT publish remains warning-clean at the qyl boundary | `.github/workflows/aot-warning-gate.yml`; `tools/smoketest.sh` | The smoke consumers publish with `PublishAot=true` and `TreatWarningsAsErrors=true`; any IL2xxx, IL3xxx, IL4xxx, or CA warning fails before runtime execution. |
| 60-item upstream-derived contract is accounted for | `tools/verify-contract-coverage-report.py`; `docs/coverage-matrix.md` | The generated matrix has 60 rows, zero missing bindings, and separates source-generated signals from NativeAOT-unreachable parity items. |
| Runtime public surface cannot drift silently | `tools/verify-public-api-baseline.py`; `PublicAPI.Shipped.txt` files | Public API analyzer baselines are committed for the runtime package projects; accidental public additions become build-visible. |
| Missing XML docs cannot slip through | `tools/verify-xml-doc-enforcement.py`; project `CS1591` warnings-as-errors | Source-generator and runtime projects generate XML docs and fail on missing public documentation. |
| Conformance processor is not an always-on production tax | `tools/verify-conformance-opt-in.py` | The verifier covers default-off, env-var opt-in, and hosting-options opt-in paths. |
| Runtime semantics survive a real WebAPI NativeAOT process | `.github/workflows/webapi-aot-demo.yml`; `tools/verify-webapi-aot-demo.py` | The demo AOT-publishes and runs an ASP.NET Core app with EFCore, HttpClient, SqlClient, traffic, and qyl activity assertions. |
| OTLP transport is not just ActivityListener-shaped JSON | `.github/workflows/otlp-collector-fixtures.yml`; `tools/verify-otlp-collector-fixtures.py` | A real OpenTelemetry SDK tracer provider exports qyl spans over OTLP/HTTP protobuf to a local collector endpoint at `/v1/traces`. |
| Hot paths have committed JIT and NativeAOT measurements | `tools/verify-benchmark-report.py`; `docs/benchmarks/*.md` | BenchmarkDotNet reports are committed with both runtimes and allocation columns for DB, EFCore, and HttpClient hot paths. |
| Release evidence is reviewable | `tools/verify-score-100-justification.py`; this document | The score document is itself gated so stale or missing evidence text breaks the full goal verifier. |

## What would invalidate this score

The score should be considered invalid if any of the following occurs:

| Invalidating condition | Required response |
|---|---|
| Any strict smoke consumer emits an IL2xxx, IL3xxx, IL4xxx, or CA warning | Fix the qyl boundary or remove the unsupported instrumentation claim. |
| `tools/verify-projectreference-behavior.py` fails to find generated interceptors | Treat ProjectReference dogfooding as broken and fix package build assets before touching docs. |
| `tools/verify-otlp-collector-fixtures.py` stops receiving `/v1/traces` with `application/x-protobuf` | Treat OTLP transport proof as broken; ActivityListener-only evidence is insufficient. |
| `docs/coverage-matrix.md` reports a missing binding | Either implement the source-generated signal or explicitly classify it as NativeAOT-unreachable parity. |
| A new public type appears without a PublicAPI baseline update | Review the API as a breaking surface change; do not shim around it. |
| A new hot path allocates before checking whether the signal is enabled/listened | Move the enabled/listener check earlier or precompute the value at options-load time. |

## AOT warning count

Strict smoke consumers publish with:

```text
dotnet publish -p:PublishAot=true -p:TreatWarningsAsErrors=true
```

The committed smoke gate reported:

```text
aot-warning-gate-ok consumer=package-reference warnings=0
aot-warning-gate-ok consumer=project-reference warnings=0
```

The NativeAOT web API demo includes EFCore and Microsoft.Data.SqlClient. That gate filters analyzer output at
the qyl boundary and reported:

```text
webapi-aot-demo-ok qyl_warnings=0
```

Residual risk: the web API demo intentionally does not claim third-party EFCore or Microsoft.Data.SqlClient
NativeAOT warning ownership. qyl-owned warnings are zero; third-party warning policy belongs to the application
boundary until those packages become fully warning-clean under the selected .NET 10 versions.

## Hot-path benchmark evidence

Committed BenchmarkDotNet reports under `docs/benchmarks/` cover JIT and NativeAOT:

| Hot path | JIT intercepted mean | NativeAOT intercepted mean | Intercepted allocation |
|---|---:|---:|---:|
| `DbCommand` | `5.1818 ns` | `8.6665 ns` | `-` |
| `EntityFrameworkCore` | `8.8453 ns` | `11.4763 ns` | `-` |
| `HttpClient.GetAsync` | `296.9 ns` | `331.9 ns` | `1176 B` |

The `HttpClient` benchmark includes the real `HttpClient` async machinery; the direct baseline allocates
`704 B`, so the measured qyl delta is `472 B`. The DB and EFCore hot paths are zero-allocation in both runtimes.

## Defect rebuttals

| Defect | Rebuttal | Closing evidence |
|---|---|---|
| E: `QylAutoInstrumentationOptions` cctor/type initialization crash | The first intercepted call is now exercised through PackageReference, ProjectReference, NativeAOT smoke, WebAPI NativeAOT, and collector-backed OTLP export. The final hardening made `InstrumentationLookupKey` null-safe and prevented stale cached packages from masking the repro. | `0fce5ab Add collector-backed OTLP fixture gate`; `tools/verify-otlp-collector-fixtures.py`; `tools/smoketest.sh`; `tools/verify-aot-autoinstrumentation-goal.py` |
| D: ProjectReference consumers do not receive interception assets | `build/` assets now mirror the `buildTransitive/` assets and `tools/verify-projectreference-behavior.py` verifies generated interceptors for a ProjectReference consumer. | `e21ca1d Verify ProjectReference autoinstrumentation assets`; `tools/verify-projectreference-behavior.py`; `tools/verify-package-layout.py` |
| Hot-path churn | Captured header names are precomputed into runtime maps, conformance work is gated, and BenchmarkDotNet reports cover DB, EFCore, and HttpClient under JIT and NativeAOT. | `d57c3cd Gate conformance and precompute captured headers`; `2b35ce3 Add hotpath benchmark gate`; `tools/verify-benchmark-report.py` |
| Always-on conformance tax | `SemConvConformanceProcessor` only runs when `QYL_CONFORMANCE_ENABLED=1` or hosting opt-in sets `EnableConformanceProcessor=true`; the default path is off. | `d57c3cd Gate conformance and precompute captured headers`; `tools/verify-conformance-opt-in.py` |

## What was invented

qyl is a C# source-interceptor substrate for .NET 10 NativeAOT auto-instrumentation. The generator uses
Roslyn interceptable locations to emit `[InterceptsLocation]` methods at compile time, so the published app
contains direct AOT-compatible call-site wrappers instead of a CLR profiler, runtime IL rewriting, startup
hooks, reflection patching, or `AssemblyLoadContext` machinery.

## Contribution artifact

The upstream-facing proposal is `docs/rfc/0001-interceptor-substrate.md`. It describes the interception
substrate, package build assets for `PackageReference` and `ProjectReference`, generated-code golden fixtures,
AOT warning gates, and the boundary between source-visible interception and runtime diagnostics.

## Residual risks

| Risk | Status |
|---|---|
| Release tag target must match the final evidence commit | The final report verifies `git rev-list -n 1 v0.3.0-pre.1` against `git rev-parse HEAD`. |
| WebAPI demo third-party warnings | qyl-owned warnings are zero. EFCore/SqlClient package analyzer warnings are treated as app/third-party boundary risk, not hidden as qyl success. |
| `HttpClient` intercepted benchmark allocates above the direct baseline | The qyl overhead is below one microsecond, but not zero-allocation because the measured path includes real `HttpClient` async request/response machinery and activity export state. DB and EFCore hot paths are zero-allocation. |
