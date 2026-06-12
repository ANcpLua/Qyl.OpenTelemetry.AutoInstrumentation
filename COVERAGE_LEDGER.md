# Coverage ledger

This ledger tracks current AOT auto-instrumentation coverage. It is not a scorecard and not a
history diary. Use it to decide what is implemented, what is verified, and what is intentionally
outside this substrate.

## Architecture contract

Current substrate:

- .NET 10 managed runtime libraries,
- Roslyn source generators,
- source-visible `[InterceptsLocation]` interceptors,
- `DiagnosticListener` and `Activity` payload consumption,
- build/buildTransitive package assets,
- `[ModuleInitializer]` bootstrap.

Forbidden product mechanisms:

- CLR profiler attach,
- startup hooks,
- runtime IL rewriting or ReJIT,
- dynamic plugin loading,
- reflection-based instrumentation dispatch.

## Contract classification

Source of truth:

- `docs/contracts/otel-dotnet-auto-60.upstream.yaml` plus `docs/contracts/qyl-aot-ownership.yaml`

Generated outputs:

- `docs/generated/qyl-aot-contract.resolved.yaml`
- `docs/generated/qyl-aot-contract.schema.json`
- `src/Qyl.AutoInstrumentation.SourceGenerators/InstrumentationContract.cs`
- `docs/coverage-matrix.md`

Current classification:

| Slice | Count | Binding |
|---|---:|---|
| Total contract items | 60 | `InstrumentationContract.TotalCount` |
| Implemented signal promises | 33 | Implemented by a declared qyl lane; source-interceptor coverage is tracked separately in the generated matrix. |
| Unsupported NativeAOT parity/dynamic signal promises | 4 | Classic ASP.NET/WCF/dynamic parity items retained with explicit unsupported status. |
| Global environment controls | 7 | Read by `QylAutoInstrumentationOptions`. |
| Instrumentation options | 16 | Read by `QylAutoInstrumentationOptions`; raw query/statement values remain behind upstream opt-in flags. |

## Verified behavior

| Area | Evidence |
|---|---|
| Package layout | `tools/verify-package-layout.py` validates analyzer/build/buildTransitive assets and forbids profiler/reflection tokens in package assets. |
| ProjectReference dogfood | `tools/verify-projectreference-behavior.py` proves the supported explicit analyzer/targets path and the unsupported bare runtime ProjectReference path. |
| Public API | `tools/verify-public-api-baseline.py` gates package API drift. |
| XML docs | `tools/verify-xml-doc-enforcement.py` gates public runtime and source-generator docs. |
| Source generator snapshots | `tools/verify-generator-snapshots.py` compares generated output to checked-in verified snapshots. |
| Source interceptor consumer | `tools/verify-source-interceptor-consumer.py` proves generated interceptors execute in managed and NativeAOT consumers. |
| Smoke consumers | `tools/smoketest.sh` packs local packages, creates PackageReference and ProjectReference consumers, and verifies output. |
| AOT warning gate | Smoke publishing fails on supported-consumer IL/trim/AOT warnings. |
| WebAPI AOT demo | `tools/verify-webapi-aot-demo.py` publishes and runs the NativeAOT web API proof. |
| OTLP-shaped fixture | `tools/verify-otlp-fixtures.py` validates normalized offline activity output. |
| Collector fixture | `tools/verify-otlp-collector-fixtures.py` validates collector-backed telemetry fixture output. |
| Consumer behavior | `tools/verify-consumer-behavior.py` checks baseline-vs-instrumented output equivalence. |
| NativeAOT consumer verified | `tools/verify-nativeaot-consumer.py` checks a normalized NativeAOT qyl HTTP client activity verified. |

The broad handoff command is:

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py
```

## Current library coverage

Detailed runtime extraction rules live in `docs/RUNTIME_SEMANTICS.md`.

| Library area | Current status |
|---|---|
| HttpClient | Real managed and NativeAOT proof through BCL diagnostic listener/activity tags. |
| ASP.NET Core | Real managed and NativeAOT trace proof through ASP.NET Core listener payloads. |
| EFCore | Real managed and NativeAOT proof with compiled model; upstream EFCore AOT warnings are app-boundary issues. |
| Grpc.Net.Client | Real managed and NativeAOT proof through public gRPC activity tags. |
| Microsoft.Data.SqlClient | Real managed and NativeAOT proof; upstream SqlClient AOT/globalization limits are app-boundary issues. |
| Classic ASP.NET/WCF dynamic parity | Explicitly unsupported for this NativeAOT/source-generator substrate. |

## Open work

- Full OTLP export normalizer for Gate A.
- Stable benchmark budget thresholds.
-
- More source-visible interceptor targets with bounded names and stable attributes.
- Better consumer diagnostics when package build assets are missing.
