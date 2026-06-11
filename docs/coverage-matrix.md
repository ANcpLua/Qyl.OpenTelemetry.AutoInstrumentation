# AOT Interceptor Coverage Matrix

This matrix is generated from `docs/otel-dotnet-auto-60-contract-items.yaml`,
`src/Qyl.AutoInstrumentation.SourceGenerators/InstrumentationContract.cs`, and
`src/Qyl.AutoInstrumentation.SourceGenerators/QylAutoInstrumentationGenerator.cs`.

It is the review artifact for the 60-item auto-instrumentation contract: upstream
contract item on the left, qyl NativeAOT interceptor/runtime status on the right.

## Counts

| Count | Value |
|---|---:|
| Total contract items | 60 |
| Source-generated signal bindings | 33 |
| Unsupported NativeAOT parity/dynamic signals | 4 |
| Runtime environment controls | 7 |
| Runtime instrumentation options | 16 |
| Missing bindings | 0 |

## Status legend

| Status | Meaning |
|---|---|
| `source_generated_signal` | The source generator has a source-visible call-site binding for this signal. |
| `unsupported_nativeaot_parity_or_dynamic_signal` | The upstream contract item is retained for parity, but it is not reachable as a NativeAOT source-interceptor signal. |
| `runtime_environment_control` | The runtime options model binds the global/signal environment control. |
| `runtime_instrumentation_option` | The runtime options model binds the instrumentation option. |
| `missing_*` | Fails the gate. |

## Matrix

| # | Contract item | Kind | Key | qyl status | Evidence |
|---:|---|---|---|---|---|
| 1 | `contract.item.01` | `signal_specific_instrumentation_promise` | `signals.traces.ADONET` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 2 | `contract.item.02` | `signal_specific_instrumentation_promise` | `signals.traces.ASPNET` | `unsupported_nativeaot_parity_or_dynamic_signal` | InstrumentationContract.UnsupportedNativeAotSignalKeys |
| 3 | `contract.item.03` | `signal_specific_instrumentation_promise` | `signals.traces.ASPNETCORE` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.AspNetCoreEndpointMap<br>QylAutoInstrumentationGenerator.InterceptorKind.AspNetCoreRequestDelegate<br>QylAutoInstrumentationGenerator.InterceptorKind.AspNetCoreWebApplicationBuilderBuild |
| 4 | `contract.item.04` | `signal_specific_instrumentation_promise` | `signals.traces.AZURE` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.AzureClient |
| 5 | `contract.item.05` | `signal_specific_instrumentation_promise` | `signals.traces.ELASTICSEARCH` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.ElasticsearchClient |
| 6 | `contract.item.06` | `signal_specific_instrumentation_promise` | `signals.traces.ELASTICTRANSPORT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.ElasticTransport |
| 7 | `contract.item.07` | `signal_specific_instrumentation_promise` | `signals.traces.ENTITYFRAMEWORKCORE` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.EntityFrameworkCoreDbContext<br>QylAutoInstrumentationGenerator.InterceptorKind.EntityFrameworkCoreQueryable |
| 8 | `contract.item.08` | `signal_specific_instrumentation_promise` | `signals.traces.GRAPHQL` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.GraphQlDocumentExecuter |
| 9 | `contract.item.09` | `signal_specific_instrumentation_promise` | `signals.traces.GRPCNETCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.GrpcNetClientAsyncUnaryCall |
| 10 | `contract.item.10` | `signal_specific_instrumentation_promise` | `signals.traces.HTTPCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.HttpClient<br>QylAutoInstrumentationGenerator.InterceptorKind.HttpWebRequest |
| 11 | `contract.item.11` | `signal_specific_instrumentation_promise` | `signals.traces.KAFKA` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.KafkaConsumer<br>QylAutoInstrumentationGenerator.InterceptorKind.KafkaProducer |
| 12 | `contract.item.12` | `signal_specific_instrumentation_promise` | `signals.traces.MASSTRANSIT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MassTransitMessageOperation |
| 13 | `contract.item.13` | `signal_specific_instrumentation_promise` | `signals.traces.MONGODB` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MongoDbCollection |
| 14 | `contract.item.14` | `signal_specific_instrumentation_promise` | `signals.traces.MYSQLCONNECTOR` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 15 | `contract.item.15` | `signal_specific_instrumentation_promise` | `signals.traces.MYSQLDATA` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 16 | `contract.item.16` | `signal_specific_instrumentation_promise` | `signals.traces.NPGSQL` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 17 | `contract.item.17` | `signal_specific_instrumentation_promise` | `signals.traces.NSERVICEBUS` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.NServiceBusMessageOperation |
| 18 | `contract.item.18` | `signal_specific_instrumentation_promise` | `signals.traces.ORACLEMDA` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 19 | `contract.item.19` | `signal_specific_instrumentation_promise` | `signals.traces.RABBITMQ` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.RabbitMqBasicPublish |
| 20 | `contract.item.20` | `signal_specific_instrumentation_promise` | `signals.traces.QUARTZ` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.QuartzJobExecute |
| 21 | `contract.item.21` | `signal_specific_instrumentation_promise` | `signals.traces.SQLCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 22 | `contract.item.22` | `signal_specific_instrumentation_promise` | `signals.traces.SQLITE` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand |
| 23 | `contract.item.23` | `signal_specific_instrumentation_promise` | `signals.traces.STACKEXCHANGEREDIS` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.StackExchangeRedisCommandAsync |
| 24 | `contract.item.24` | `signal_specific_instrumentation_promise` | `signals.traces.WCFCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.WcfClient |
| 25 | `contract.item.25` | `signal_specific_instrumentation_promise` | `signals.traces.WCFCORE` | `unsupported_nativeaot_parity_or_dynamic_signal` | InstrumentationContract.UnsupportedNativeAotSignalKeys |
| 26 | `contract.item.26` | `signal_specific_instrumentation_promise` | `signals.traces.WCFSERVICE` | `unsupported_nativeaot_parity_or_dynamic_signal` | InstrumentationContract.UnsupportedNativeAotSignalKeys |
| 27 | `contract.item.27` | `signal_specific_instrumentation_promise` | `signals.metrics.ASPNET` | `unsupported_nativeaot_parity_or_dynamic_signal` | InstrumentationContract.UnsupportedNativeAotSignalKeys |
| 28 | `contract.item.28` | `signal_specific_instrumentation_promise` | `signals.metrics.ASPNETCORE` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 29 | `contract.item.29` | `signal_specific_instrumentation_promise` | `signals.metrics.HTTPCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.HttpClient<br>QylAutoInstrumentationGenerator.InterceptorKind.HttpWebRequest<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 30 | `contract.item.30` | `signal_specific_instrumentation_promise` | `signals.metrics.NETRUNTIME` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 31 | `contract.item.31` | `signal_specific_instrumentation_promise` | `signals.metrics.NPGSQL` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 32 | `contract.item.32` | `signal_specific_instrumentation_promise` | `signals.metrics.NSERVICEBUS` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter<br>QylAutoInstrumentationGenerator.InterceptorKind.NServiceBusMessageOperation |
| 33 | `contract.item.33` | `signal_specific_instrumentation_promise` | `signals.metrics.PROCESS` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 34 | `contract.item.34` | `signal_specific_instrumentation_promise` | `signals.metrics.SQLCLIENT` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.DbCommand<br>QylAutoInstrumentationGenerator.InterceptorKind.MeterProviderBuilderAddMeter |
| 35 | `contract.item.35` | `signal_specific_instrumentation_promise` | `signals.logs.ILOGGER` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.ILoggerExtensionLog<br>QylAutoInstrumentationGenerator.InterceptorKind.ILoggerLog |
| 36 | `contract.item.36` | `signal_specific_instrumentation_promise` | `signals.logs.LOG4NET` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.Log4NetLogger |
| 37 | `contract.item.37` | `signal_specific_instrumentation_promise` | `signals.logs.NLOG` | `source_generated_signal` | InstrumentationContract.TryGetSourceGeneratedSignal<br>QylAutoInstrumentationGenerator.InterceptorKind.NLogLogger |
| 38 | `contract.item.38` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 39 | `contract.item.39` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 40 | `contract.item.40` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_TRACES_{0}_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 41 | `contract.item.41` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 42 | `contract.item.42` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_METRICS_{0}_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 43 | `contract.item.43` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 44 | `contract.item.44` | `global_environment_control` | `global_environment_controls.OTEL_DOTNET_AUTO_LOGS_{0}_INSTRUMENTATION_ENABLED` | `runtime_environment_control` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 45 | `contract.item.45` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 46 | `contract.item.46` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 47 | `contract.item.47` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 48 | `contract.item.48` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 49 | `contract.item.49` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 50 | `contract.item.50` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 51 | `contract.item.51` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 52 | `contract.item.52` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 53 | `contract.item.53` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 54 | `contract.item.54` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 55 | `contract.item.55` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 56 | `contract.item.56` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 57 | `contract.item.57` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 58 | `contract.item.58` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 59 | `contract.item.59` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |
| 60 | `contract.item.60` | `instrumentation_option` | `instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED` | `runtime_instrumentation_option` | QylAutoInstrumentationOptions<br>tools/verify-environment-options-behavior.py |

// validated 2026-06-05 by tools/verify-contract-coverage-report.py
