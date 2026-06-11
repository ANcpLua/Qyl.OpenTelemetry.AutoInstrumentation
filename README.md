# qyl-dotnet-autoinstrumentation

A .NET 10, NativeAOT-ready, vendor-neutral auto-instrumentation library.

The current product is pure managed code. It uses Roslyn source generators,
`DiagnosticListener`, `ActivitySource`, `Meter`, build-transitive package assets, and
`[ModuleInitializer]` bootstrapping. It does not use a CLR profiler, startup hook,
runtime IL rewrite, attach tool, plugin store, or reflection-based dispatch.

## What this repository ships

| Package project | Purpose |
|---|---|
| `src/Qyl.AutoInstrumentation` | Core runtime APIs, semantic helpers, metric helpers, generated-interceptor targets, and package build assets. |
| `src/Qyl.AutoInstrumentation.SourceGenerators` | Build-time Roslyn generator for contract-gated source interceptors and generated semantic registries. |
| `src/Qyl.AutoInstrumentation.DiagnosticListeners` | Shared DiagnosticListener substrate and runtime payload readers for framework/library events. |
| `src/Qyl.AutoInstrumentation.Hosting` | General application bootstrap package with build-transitive module initializer and service registration. |
| `src/Qyl.AutoInstrumentation.EntityFrameworkCore` | EFCore-specific bootstrap/listener package kept separate so EFCore dependencies and AOT warnings do not leak into non-EF apps. |
| `src/Qyl.AutoInstrumentation.SqlClient` | Microsoft.Data.SqlClient-specific bootstrap/listener package kept separate for the same dependency-boundary reason. |

The repository also contains real consumer demos, source-generator snapshots, NativeAOT
smoke consumers, OTLP-shaped fixtures, and package-layout checks. These are not decorative;
they are the product proof surface.

## Install shape

Use package references. A normal app should not call qyl boot APIs just to get baseline
instrumentation.

```xml
<ItemGroup>
  <PackageReference Include="Qyl.AutoInstrumentation.Hosting" Version="0.3.0-pre.1" />
</ItemGroup>
```

Add package-specific references only when the app uses those libraries:

```xml
<ItemGroup>
  <PackageReference Include="Qyl.AutoInstrumentation.EntityFrameworkCore" Version="0.3.0-pre.1" />
  <PackageReference Include="Qyl.AutoInstrumentation.SqlClient" Version="0.3.0-pre.1" />
</ItemGroup>
```

`PackageReference` is the supported zero-code path because NuGet carries the analyzer,
`build/`, and `buildTransitive/` assets into the consuming compilation. A bare runtime
`ProjectReference` is not equivalent: MSBuild does not automatically flow analyzer and
build assets from a referenced project. The verified dogfooding path explicitly references
the runtime project, the generator project as an analyzer, and the core targets file.

## How bootstrapping works

1. Build assets enable the interceptor namespace and include a local
   `InterceptsLocationAttribute` source file for consumers.
2. The source generator discovers supported source-visible call-sites and emits ordinary C#
   interceptors annotated with `[InterceptsLocation]`.
3. Package bootstrap code runs through `[ModuleInitializer]` and activates qyl once per
   process.
4. Runtime listeners consume values supplied by framework/library events or current
   activities. Missing values stay missing.
5. Emitted telemetry uses stable OpenTelemetry attributes by default. Raw sensitive values
   such as `url.full`, `url.path`, and `db.query.text` are gated off unless explicitly enabled.

## Supported proof surface

| Area | Evidence |
|---|---|
| HttpClient | Real managed and NativeAOT demo using BCL `HttpHandlerDiagnosticListener` events. |
| ASP.NET Core | Real managed and NativeAOT Kestrel demo using framework listener payloads. |
| EFCore | Real managed and NativeAOT demo using typed command event payloads and an EF compiled model. |
| Grpc.Net.Client | Real managed and NativeAOT demo using public gRPC activity tags. |
| Microsoft.Data.SqlClient | Real managed and NativeAOT demo using SqlClient diagnostic payloads; SqlClient's own AOT warnings are treated as an app-side library boundary. |
| Confluent.Kafka | Real managed and NativeAOT demo using source-generated producer/consumer interceptors against a real broker; NativeAOT needs an app-side `TrimmerRootAssembly` for Confluent.Kafka. |
| RabbitMQ.Client | Real managed and NativeAOT demo using source-generated `BasicPublishAsync` interceptors against a real broker, with publisher confirmations proving the error path. |
| MongoDB.Driver | Real managed and NativeAOT demo using source-generated `IMongoCollection<T>` interceptors against a real server; NativeAOT needs app-side `TrimmerRootAssembly` roots for MongoDB.Bson/MongoDB.Driver. |
| Package boot | Temporary PackageReference consumers prove zero-code bootstrap for Hosting, EFCore, and SqlClient packages. |
| ProjectReference dogfood | Explicit analyzer/targets dogfooding path proves local source-tree development without pretending a bare ProjectReference is enough. |

## Runtime rules

- Do not invent runtime values. If the instrumented library did not expose a value, qyl does
  not synthesize it from guesses.
- Do not put full URLs, request paths, query strings, IDs, exception messages, or arbitrary
  caller text in span names.
- Prefer bounded attributes: route templates over raw paths, database operation/summary over
  raw statements, well-known error identifiers over exception messages.
- Consume deprecated OpenTelemetry aliases as inputs only; do not re-emit them as canonical
  output.
- Keep metric instruments and activity sources process-level, not per request.
- Keep conformance/self-telemetry opt-in when it adds per-span work.

## Configuration

`QylAutoInstrumentationOptions` reads the 60-item OpenTelemetry .NET auto-instrumentation
contract mirrored in `docs/otel-dotnet-auto-60-contract-items.yaml`. Contract coverage is
reported in `docs/coverage-matrix.md`.

Sensitive capture is off by default:

```bash
QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES=true
```

The conformance processor is off by default:

```bash
QYL_CONFORMANCE_ENABLED=1
```

## Verification

The broad handoff gate is:

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py
```

Important focused gates:

```bash
python3 tools/verify-package-layout.py
python3 tools/verify-projectreference-behavior.py
python3 tools/verify-source-interceptor-consumer.py
python3 tools/verify-nativeaot-consumer-golden.py
python3 tools/verify-otlp-golden-fixtures.py
python3 tools/verify-otlp-collector-fixtures.py
python3 tools/verify-webapi-aot-demo.py
bash tools/smoketest.sh
```

Benchmark measurements live in `benchmarks/Qyl.AutoInstrumentation.Benchmarks`. They are
measurement evidence, not product code.

## NativeAOT boundaries

- The source generator targets `netstandard2.0` because it runs inside the compiler. It is
  excluded from NativeAOT publish graphs; consumers receive its analyzer output at build time.
- EFCore NativeAOT requires a compiled model. The compiled model in the demo is generated by
  EF tooling and may contain `System.Reflection`; that is not qyl runtime dispatch.
- Microsoft.Data.SqlClient currently emits its own trim/AOT warnings and does not support
  invariant globalization. qyl keeps this boundary explicit instead of hiding it in Hosting.
- Classic ASP.NET and WCF dynamic parity items from the upstream contract are explicitly
  unsupported for this NativeAOT/source-generator substrate.

## Current limitations and next work

- Finish a full OTLP export normalizer instead of only OTLP-shaped committed fixtures.
- Add automated update flow for the 60-item contract manifest and generated coverage matrix.
- Expand source-generator target coverage only where call-sites are source-visible and stable.
- Add benchmark budget gates once the measurement noise floor is stable across CI runners.
- Improve user experience around package selection, ProjectReference dogfooding, and failure
  messages when build assets are missing.

See `CHANGELOG.md` for the synthesized five-commit history and continuation plan.
