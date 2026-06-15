# Qyl.OpenTelemetry.AutoInstrumentation

A .NET 10, NativeAOT-ready, vendor-neutral auto-instrumentation library.

The current product is pure managed code. It uses Roslyn source generators,
`DiagnosticListener`, `ActivitySource`, `Meter`, build-transitive package assets, and
`[ModuleInitializer]` bootstrapping. It does not use a CLR profiler, startup hook,
runtime IL rewrite, attach tool, plugin store, or reflection-based dispatch.

## What this repository ships

| Package project | Purpose |
|---|---|
| `src/Qyl.OpenTelemetry.AutoInstrumentation` | Core runtime APIs, semantic helpers, metric helpers, generated-interceptor targets, and package build assets. |
| `src/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators` | Build-time Roslyn generator for contract-gated source interceptors and generated semantic registries. |
| `src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners` | Shared DiagnosticListener substrate and runtime payload readers for framework/library events. |
| `src/Qyl.OpenTelemetry.AutoInstrumentation.Hosting` | General application bootstrap package with build-transitive module initializer and service registration. |
| `src/Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore` | EFCore-specific bootstrap/listener package kept separate so EFCore dependencies and AOT warnings do not leak into non-EF apps. |
| `src/Qyl.OpenTelemetry.AutoInstrumentation.SqlClient` | Microsoft.Data.SqlClient-specific bootstrap/listener package kept separate for the same dependency-boundary reason. |

The repository also contains real consumer demos, source-generator snapshots, NativeAOT
smoke consumers, OTLP-shaped fixtures, and package-layout checks. These are not decorative;
they are the product proof surface.

## Install shape

Use package references. A normal app should not call qyl boot APIs just to get baseline
instrumentation.

```xml
<ItemGroup>
  <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="0.3.0-pre.1" />
</ItemGroup>
```

Add package-specific references only when the app uses those libraries:

```xml
<ItemGroup>
  <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore" Version="0.3.0-pre.1" />
  <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.SqlClient" Version="0.3.0-pre.1" />
</ItemGroup>
```

`PackageReference` is the supported zero-code path because NuGet carries the analyzer,
`build/`, and `buildTransitive/` assets into the consuming compilation. A bare runtime
`ProjectReference` is not equivalent: MSBuild does not automatically flow analyzer and
build assets from a referenced project. The verified dogfooding path explicitly references
the runtime project, the generator project as an analyzer, and the core targets file.

## How bootstrapping works

1. Build assets enable the interceptor namespace (`InterceptorsNamespaces`) in the consumer
   compilation.
2. The source generator discovers supported source-visible call-sites, emits ordinary C#
   interceptors annotated with `[InterceptsLocation]`, and emits the file-local
   `InterceptsLocationAttribute` definition into the same generated file.
3. Package bootstrap code runs through `[ModuleInitializer]` and activates qyl once per
   process.
4. Runtime listeners consume values supplied by framework/library events or current
   activities. Missing values stay missing.
5. Emitted telemetry uses stable OpenTelemetry attributes by default. Query-string values in
   `url.full`/`url.query` are redacted to `Redacted` (keys stay); raw query values and
   `db.query.text` are upstream-flag opt-ins.

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
| StackExchange.Redis | Real managed and NativeAOT demo using source-generated `IDatabaseAsync` command interceptors against a real server; AOT publish is warning-free with no roots. |
| Quartz | Real managed and NativeAOT demo: a real scheduler fires a job whose source-visible `IJob.Execute` delegation calls are intercepted; scheduler-internal dispatch is an explicit non-goal. NativeAOT needs `TrimmerRootAssembly=Quartz`. |
| MassTransit | Real managed and NativeAOT demo (MassTransit 8.x OSS line) using source-generated `Publish`/`Send` interceptors against a real RabbitMQ broker; NativeAOT needs an app-side source-generated `JsonSerializerContext` chained into MassTransit's serializer. |
| NServiceBus | Real managed demo using source-generated `IMessageSession` interceptors on a real LearningTransport endpoint with a handler round-trip; NativeAOT is structurally blocked by NServiceBus's Reflection.Emit proxy creator — an NServiceBus library boundary. |
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
contract resolved from `docs/contracts/otel-dotnet-auto-60.upstream.yaml` plus
`docs/contracts/qyl-aot-ownership.yaml`. Contract coverage is reported in
`docs/coverage-matrix.md`.

Query values are redacted by default; the upstream flags switch redacted to raw:

```bash
OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION=true
OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=true
```

Custom metric meters can be appended to the source-generated meter registration path with the
upstream additional-sources option. Names are case-sensitive and de-duplicated:

```bash
OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES=YourCompany.CustomMeter,Other.Meter
```

The conformance processor is off by default:

```bash
QYL_CONFORMANCE_ENABLED=1
```

<!-- qyl-contract-summary:start -->
## Generated contract ownership summary

<!-- <auto-generated/> -->
<!-- Regenerate with `python3 tools/generate-contract-artifacts.py --write`. -->

The upstream OpenTelemetry contract, qyl mechanism ownership, and generated resolved contract are split deliberately:

| Contract layer | Path | Role |
|---|---|---|
| Upstream contract | `docs/contracts/otel-dotnet-auto-60.upstream.yaml` | Raw 60-item OpenTelemetry .NET auto-instrumentation contract with per-item source anchors. |
| qyl ownership overlay | `docs/contracts/qyl-aot-ownership.yaml` | qyl lane, status, call-site visibility, payload access, evidence, and conformance semantics. |
| Resolved generated contract | `docs/generated/qyl-aot-contract.resolved.yaml` | Joined model used to generate schema, C# contract data, matrix, and conformance plan. |

| # | Key | Upstream source | Lane | qyl status | Visibility | Payload | Evidence |
|---:|---|---|---|---|---|---|---|
| 1 | `signals.traces.ADONET` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L144-L180) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 2 | `signals.traces.ASPNET` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L145-L184) | `unsupported_nativeaot` | `unsupported_nativeaot` | `user_code` | `reflection_required` | `none` |
| 3 | `signals.traces.ASPNETCORE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L146) | `runtime_public_telemetry` | `implemented` | `both` | `typed_public` | `verified_nativeaot` |
| 4 | `signals.traces.AZURE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L147-L186) | `framework_initialization` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 5 | `signals.traces.ELASTICSEARCH` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L148-L189) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 6 | `signals.traces.ELASTICTRANSPORT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L149) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 7 | `signals.traces.ENTITYFRAMEWORKCORE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L150-L195) | `runtime_public_telemetry` | `implemented` | `both` | `typed_public` | `verified_nativeaot` |
| 8 | `signals.traces.GRAPHQL` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L151) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 9 | `signals.traces.GRPCNETCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L152) | `runtime_public_telemetry` | `implemented` | `both` | `typed_public` | `verified_nativeaot` |
| 10 | `signals.traces.HTTPCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L153) | `runtime_public_telemetry` | `implemented` | `both` | `typed_public` | `verified_nativeaot` |
| 11 | `signals.traces.KAFKA` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L154-L198) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 12 | `signals.traces.MASSTRANSIT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L155) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 13 | `signals.traces.MONGODB` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L156-L201) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 14 | `signals.traces.MYSQLCONNECTOR` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L157) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 15 | `signals.traces.MYSQLDATA` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L158) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 16 | `signals.traces.NPGSQL` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L159-L195) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 17 | `signals.traces.NSERVICEBUS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L160) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_managed` |
| 18 | `signals.traces.ORACLEMDA` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L161) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 19 | `signals.traces.RABBITMQ` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L162-L204) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 20 | `signals.traces.QUARTZ` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L163) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 21 | `signals.traces.SQLCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L164-L210) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 22 | `signals.traces.SQLITE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L165) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 23 | `signals.traces.STACKEXCHANGEREDIS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L166-L213) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 24 | `signals.traces.WCFCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L167) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_managed` |
| 25 | `signals.traces.WCFCORE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L168) | `unsupported_nativeaot` | `unsupported_nativeaot` | `library_internal` | `reflection_required` | `none` |
| 26 | `signals.traces.WCFSERVICE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L169) | `unsupported_nativeaot` | `unsupported_nativeaot` | `library_internal` | `reflection_required` | `none` |
| 27 | `signals.metrics.ASPNET` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L223) | `unsupported_nativeaot` | `unsupported_nativeaot` | `user_code` | `reflection_required` | `none` |
| 28 | `signals.metrics.ASPNETCORE` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L224) | `runtime_public_telemetry` | `implemented` | `library_internal` | `typed_public` | `verified_nativeaot` |
| 29 | `signals.metrics.HTTPCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L225) | `runtime_public_telemetry` | `implemented` | `both` | `typed_public` | `verified_nativeaot` |
| 30 | `signals.metrics.NETRUNTIME` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L226) | `runtime_public_telemetry` | `implemented` | `library_internal` | `typed_public` | `verified_nativeaot` |
| 31 | `signals.metrics.NPGSQL` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L227) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 32 | `signals.metrics.NSERVICEBUS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L228) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_managed` |
| 33 | `signals.metrics.PROCESS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L229) | `runtime_public_telemetry` | `implemented` | `library_internal` | `typed_public` | `verified_nativeaot` |
| 34 | `signals.metrics.SQLCLIENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L230-L236) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 35 | `signals.logs.ILOGGER` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L244-L251) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 36 | `signals.logs.LOG4NET` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L245-L254) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 37 | `signals.logs.NLOG` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L246-L257) | `source_interceptor` | `implemented` | `user_code` | `not_applicable` | `verified_nativeaot` |
| 38 | `global_environment_controls.OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L128) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 39 | `global_environment_controls.OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L129) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 40 | `global_environment_controls.OTEL_DOTNET_AUTO_TRACES_{0}_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L120-L130) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 41 | `global_environment_controls.OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L131) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 42 | `global_environment_controls.OTEL_DOTNET_AUTO_METRICS_{0}_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L120-L132) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 43 | `global_environment_controls.OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L133) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 44 | `global_environment_controls.OTEL_DOTNET_AUTO_LOGS_{0}_INSTRUMENTATION_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L120-L134) | `environment_control` | `control_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 45 | `instrumentation_options.OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT` | [60-item source row](https://github.com/open-telemetry/opentelemetry.io/blob/db8337edbbac824aebbb330acea18a7042b38806/content/en/docs/zero-code/dotnet/instrumentations.md#L156)<br>[current removal note](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/CHANGELOG.md#L414-L418) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 46 | `instrumentation_options.OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L263) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 47 | `instrumentation_options.OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L264) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 48 | `instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT` | [60-item source row](https://github.com/open-telemetry/opentelemetry.io/blob/db8337edbbac824aebbb330acea18a7042b38806/content/en/docs/zero-code/dotnet/instrumentations.md#L159)<br>[current removal note](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/CHANGELOG.md#L416-L418) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 49 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L265) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 50 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L266) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 51 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L267) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 52 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L268) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 53 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L269) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 54 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L270) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 55 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L271) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 56 | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L272) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 57 | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L273) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 58 | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L274) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 59 | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L275) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |
| 60 | `instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED` | [current config](https://github.com/open-telemetry/opentelemetry-dotnet-instrumentation/blob/611b815a62a66b5ece634337328757444fb9c2e9/docs/config.md#L276-L284) | `instrumentation_option` | `option_bound` | `not_applicable` | `not_applicable` | `option_bound` |

<!-- qyl-contract-summary:end -->


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
python3 tools/verify-nativeaot-consumer.py
python3 tools/verify-otlp-fixtures.py
python3 tools/verify-otlp-collector-fixtures.py
python3 tools/verify-webapi-aot-demo.py
bash tools/smoketest.sh
```

Benchmark measurements live in `benchmarks/Qyl.OpenTelemetry.AutoInstrumentation.Benchmarks`. They are
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
