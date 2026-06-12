# Runtime Semantics

This is the runtime auto-instrumentation lane. Semantic-convention package generation is a
separate concern.

The agent must find values at runtime from the instrumented library's own APIs, DiagnosticSource
events, Activity tags, or error paths. Zero-code means the app does not call qyl boot APIs or add
instrumentation code; qyl code lives in the package bootstrap and listeners.

## Global Rules

- Emit stable OpenTelemetry attributes by default.
- Consume deprecated aliases as input only; do not re-emit deprecated keys.
- Do not invent values. Missing runtime values stay missing.
- Redact query-string values and gate raw statement text by default; raw values are explicit
  upstream-flag opt-ins.
- Keep span names bounded. Do not include full URLs, paths, query text, IDs, or exception messages.
- Prefer low-cardinality summaries: route templates over paths, database operation/summary over raw
  SQL, and well-known error identifiers over exception messages.

## Privacy

Attributes that can carry user data follow the upstream OpenTelemetry .NET model: redaction
operates on query/statement values, never on attribute emission.

| Attribute | Default | Raw opt-in |
|---|---|---|
| `url.full` (client spans) | Always emitted; query values redacted (`?token=Redacted`, keys stay). | `OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION=true` |
| `url.path` (server spans) | Always emitted. | — |
| `url.query` (server spans) | Always emitted; values redacted (keys stay). | `OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION=true` |
| `db.namespace` | Always emitted when the connection exposes it. | — |
| `db.query.text` | Off by default. | `OTEL_DOTNET_AUTO_<ID>_SET_DBSTATEMENT_FOR_TEXT=true` |

The bootstrap sets the `System.Net.Http.DisableUriRedaction` AppContext switch: the BCL
otherwise collapses query strings to `*` in its distributed-tracing tags before qyl can apply
value-level redaction, so the listener path could never produce upstream-shaped `url.full`.

## Library Matrix

| Library | Runtime source | Event/error path knowledge | Emitted stable defaults | Status |
|---|---|---|---|---|
| `System.Net.Http.HttpClient` | `HttpHandlerDiagnosticListener`; `Activity.Current` tags on `System.Net.Http.HttpRequestOut.Stop`. | Real local .NET 10 proof covers 503 response and connection failure. Error values observed: status-code string such as `503`, and BCL low-cardinality `connection_error`. | `http.request.method`, `url.full` (query values redacted), `server.address`, `server.port`, `http.response.status_code`, `error.type`. | Real managed + NativeAOT proof. |
| ASP.NET Core | `Microsoft.AspNetCore` listener; `HttpContext` payload on `Microsoft.AspNetCore.Hosting.HttpRequestIn.Stop`; .NET 10 built-in metrics are collected from the framework meters. | Real local .NET 10 Kestrel proof covers 204 route response and unhandled-exception 500 response. Route source is `RouteEndpoint.RoutePattern.RawText`; `Request.Path` is emitted as `url.path`. For .NET 10 ASP.NET Core metrics, the contract source is the built-in `Microsoft.AspNetCore.Components` and `Microsoft.AspNetCore.Components.Lifecycle` meters; qyl must not re-emit that metric from `NavigationManager.NavigateTo` because the required component type and route attributes are not available from the source-visible call-site. | Traces: `http.request.method`, `http.route`, `url.path`, `url.query` (values redacted), `http.response.status_code`, `error.type` for 5xx. Built-in metrics: `aspnetcore.components.navigate` plus lifecycle metrics such as `aspnetcore.components.update_parameters.duration`, `aspnetcore.components.render_diff.duration`, and `aspnetcore.components.render_diff.size`, with framework-owned attributes. | Real managed + NativeAOT trace proof; real managed .NET 10 components metrics proof; metrics are framework-owned built-in meter collection, not qyl re-emission. |
| EFCore | `Qyl.AutoInstrumentation.EntityFrameworkCore` package; `Microsoft.EntityFrameworkCore` listener; typed `CommandExecutedEventData` and `CommandErrorEventData` payloads. | Real .NET 10 Sqlite proof covers `ExecuteSqlRaw` insert/update and provider command error. Extracted values come from `Command`, `CommandSource`, `Context.Database.ProviderName`, `DbConnection.Database`, and provider exception type. Plain EFCore NativeAOT without a compiled model fails at runtime; compiled-model NativeAOT runs, but EFCore itself still emits trim/AOT warnings. | `db.system.name`, `db.namespace`, `db.operation.name`, `db.query.summary`, `error.type`; `db.query.text` behind the upstream `SET_DBSTATEMENT_FOR_TEXT` flag. | Real managed + NativeAOT runtime proof, with explicit EFCore app-side warning boundary. |
| SqlClient | `Qyl.AutoInstrumentation.SqlClient` package; `SqlClientDiagnosticListener`; Microsoft.Data.SqlClient command payload key-value entries carrying `SqlCommand` and `SqlException`. The shared host consumes only the synthetic `qyl.db.sqlclient` event. Compile-time SqlClient command emitters remain contract items. | Real SQL Server proof covers `WriteCommandAfter` for CREATE/INSERT/SELECT and `WriteCommandError` for SQL Server error 208. Extracted values come from `SqlCommand.CommandText`, `CommandType`, `Connection.Database`, `Connection.DataSource`, and `SqlException.Number`. `System.Data.SqlClient` is still pending. NativeAOT runs, but Microsoft.Data.SqlClient 7.0.1 itself emits trim/AOT warnings and does not support `InvariantGlobalization=true`. | `db.system.name=microsoft.sql_server`, `db.namespace`, `db.operation.name`, `db.query.summary`, `server.address`, `server.port`, `error.type` for SQL errors; `db.query.text` behind the upstream `SET_DBSTATEMENT_FOR_TEXT` flag. | Real managed + NativeAOT runtime proof for Microsoft.Data.SqlClient, with explicit SqlClient app-side warning and globalization boundary. |
| MySql.Data | Source-generated interceptors on source-visible `MySqlCommand` execution calls; no DiagnosticListener involved. | Real MySql.Data 9.7 proof covers two deterministic unconnected command failures (`error.type=InvalidOperationException`) without requiring a MySQL server. The proof validates provider-specific command classification and NativeAOT viability, not a server round-trip. | `db.system.name=mysql`, `db.operation.name`, `error.type` on failure; `db.query.text` is not emitted by default. | Real managed + NativeAOT source-interceptor proof. |
| Oracle.ManagedDataAccess | Source-generated interceptors on source-visible `OracleCommand` execution calls; no DiagnosticListener involved. | Real Oracle.ManagedDataAccess.Core 23.9 proof covers `ExecuteNonQuery()` and `ExecuteScalar()` deterministic unconnected command failures (`error.type=InvalidOperationException`) without requiring an Oracle server. The proof validates provider-specific command classification and NativeAOT viability, not a server round-trip. | `db.system.name=oracle`, `db.operation.name`, `db.query.summary`, `error.type` on failure; `db.query.text` is not emitted by default. | Real managed + NativeAOT source-interceptor proof. |
| Elastic.Clients.Elasticsearch | Source-generated interceptors on source-visible Elasticsearch client calls; no DiagnosticListener involved. | Real Elasticsearch 8.9 client proof covers sync `Ping()` and async `PingAsync()` connection failures (`error.type=TransportException`). NativeAOT runs, but `Elastic.Clients.Elasticsearch`/`Elastic.Transport` emit trim/AOT/single-file warnings from their own metadata/version helpers. | `db.system.name=elasticsearch`, `db.operation.name=request`, `error.type` on failure; `db.query.text` is not emitted by default. | Real managed + NativeAOT proof, with explicit Elastic app-side warning boundary. |
| GraphQL.NET | Source-generated interceptors on source-visible `IDocumentExecuter.ExecuteAsync`; no DiagnosticListener involved. | Real GraphQL.NET proof covers a successful `ExecutionOptions` query and a deterministic null-options failure (`error.type=ArgumentNullException`). The operation name is taken from `ExecutionOptions.OperationName`; document capture is opt-in only. | `graphql.operation.name`, `error.type` on failure; `graphql.document` only when `OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT=true`. | Real managed + NativeAOT proof. |
| Grpc.Net.Client | `Grpc.Net.Client` listener; real `Grpc.Net.Client.GrpcOut.Stop` activity tags `grpc.method` and `grpc.status_code`; synthetic aliases still consumed when supplied. | Real .NET 10 proof covers successful unary call (`grpc.status_code=0`) and connection failure (`grpc.status_code=14`, `Unavailable`). `grpc.method=/qyl.LiveProbe/Collect` is split into `rpc.service=qyl.LiveProbe` and `rpc.method=Collect`. The AOT-safe public activity tags do not expose `server.address`/`server.port`; those are emitted only when supplied by aliases. | `rpc.system.name=grpc`, `rpc.service`, `rpc.method`, `rpc.grpc.status_code`, `error.type` for non-zero status; optional `server.address`/`server.port` only when supplied. | Real managed + NativeAOT proof. |
| Confluent.Kafka | Source-generated interceptors on `IProducer<K,V>.Produce`/`ProduceAsync` and `IConsumer<K,V>.Consume`; no DiagnosticListener involved. | Real Kafka 4.1 broker proof covers async produce, sync produce, consume polling, and unreachable-broker produce failure (`error.type=ProduceException`). Consumer spans are per `Consume` call, including empty polls. NativeAOT requires `TrimmerRootAssembly=Confluent.Kafka` because librdkafka delegate wiring resolves the library's own `NativeMethods` members through reflection that trimming removes; Confluent.Kafka 2.14.2 also emits its own IL2104 trim warning. | `messaging.system=kafka`, `messaging.operation.type=send`/`receive`, `messaging.operation.name`, `error.type` on failure. | Real managed + NativeAOT proof, with explicit Confluent.Kafka app-side trimmer-root boundary. |
| RabbitMQ.Client | Source-generated interceptors on `IChannel.BasicPublishAsync`; no DiagnosticListener involved. | Real RabbitMQ 4 broker proof covers default-exchange publish to a declared queue and missing-exchange publish failure. Publisher confirmations are enabled so the failed publish faults the awaited `ValueTask` deterministically (`error.type=AlreadyClosedException`). RabbitMQ.Client 7.2.1 publishes under NativeAOT without warnings. | `messaging.system=rabbitmq`, `messaging.operation.type=send`, `messaging.operation.name=publish`, `error.type` on failure. | Real managed + NativeAOT proof. |
| MongoDB.Driver | Source-generated interceptors on `IMongoCollection<T>` command methods (`InsertOneAsync`, `CountDocumentsAsync`, `DeleteManyAsync`, ...); no DiagnosticListener involved. | Real MongoDB 8 proof covers insert, count, delete, and duplicate-key insert failure (`error.type=MongoWriteException`). Operation names are normalized (`insert`, `count`, `delete`). NativeAOT requires `TrimmerRootAssembly` for `MongoDB.Bson` and `MongoDB.Driver`: the untrimmed-rooted binary runs, while the trimmed binary hangs in driver initialization before any I/O; the driver also emits its own IL2104/IL3053 warnings. | `db.system.name=mongodb`, `db.operation.name`, `db.query.summary`, `error.type` on failure; `db.query.text` behind the upstream `SET_DBSTATEMENT_FOR_TEXT` flag. | Real managed + NativeAOT proof, with explicit MongoDB.Driver app-side trimmer-root boundary. |
| StackExchange.Redis | Source-generated interceptors on `IDatabaseAsync` commands (`StringSetAsync`, `StringGetAsync`, `KeyDeleteAsync`, `ExecuteAsync`, ...); no DiagnosticListener involved. | Real Redis 8 proof covers SET/GET/DEL round-trip and an unknown `ExecuteAsync` command failure (`error.type=RedisServerException`). StackExchange.Redis 2.13.17 publishes under NativeAOT with zero IL warnings and no trimmer roots. | `db.system.name=redis`, `db.operation.name` (Redis command name), `db.query.summary`, `error.type` on failure. | Real managed + NativeAOT proof. |
| Quartz | Source-generated interceptors on source-visible `IJob.Execute` calls; no DiagnosticListener involved. | Real in-process Quartz 3.18 scheduler proof: the scheduler fires a job that delegates to inner jobs through source-visible `Execute` calls, covering success and a throwing job (`error.type=InvalidOperationException`). Scheduler-internal job dispatch happens inside Quartz.dll and is structurally not reachable by source interception; only source-visible `Execute` composition calls are. NativeAOT requires `TrimmerRootAssembly=Quartz` because Quartz instantiates its type-load helper and jobs through `Activator.CreateInstance`; Quartz also emits its own IL2104/IL3053 warnings. | `qyl.instrumentation.domain=job.quartz`, `error.type` on failure; span kind Internal. | Real managed + NativeAOT proof for source-visible call sites, with explicit dispatch-boundary statement and Quartz app-side trimmer-root boundary. |
| MassTransit | Source-generated interceptors on `IPublishEndpoint.Publish` and `ISendEndpoint.Send`; no DiagnosticListener involved. | Real RabbitMQ 4 broker proof on the MassTransit 8.x open-source line (9.x is commercial) covers publish, queue send, and a deterministic publish failure: MassTransit rejects namespace-less message types inside the intercepted call (`error.type=ArgumentException`). NativeAOT works when the app chains a source-generated `JsonSerializerContext` into MassTransit's serializer options; without it message serialization throws under AOT. MassTransit emits its own IL trim/AOT warnings. | `messaging.system=masstransit`, `messaging.operation.type=send`, `messaging.operation.name=publish`/`send`, `error.type` on failure. | Real managed + NativeAOT proof, with explicit app-side STJ source-generation requirement. |
| NServiceBus | Source-generated interceptors on `IMessageSession.Publish`/`Send`; no DiagnosticListener involved. | Real NServiceBus 10 endpoint proof on the LearningTransport with assembly scanning disabled and explicit handler registration: publish, routed send with a real handler round-trip, and an unrouted send failure (`error.type=Exception`; NServiceBus throws the base exception type for missing routes). NativeAOT is structurally blocked by NServiceBus itself: endpoint creation unconditionally constructs a Reflection.Emit proxy creator (`MessageMapper` → `ConcreteProxyCreator`), which cannot run under NativeAOT. Vendor-confirmed: the NServiceBus 10.2.0 announcement (discuss.particular.net topic 4626) calls scanning-free registration "a foundation for aligning with ahead-of-time compilation and trimming strategies in the future, even though full AOT and trimming compliance is not yet available." | `messaging.system=nservicebus`, `messaging.operation.type=send`, `messaging.operation.name=publish`/`send`, `error.type` on failure. | Real managed proof; NativeAOT is an explicit NServiceBus library boundary (Reflection.Emit at endpoint creation); tracked upstream: [Particular/NServiceBus#7817](https://github.com/Particular/NServiceBus/issues/7817). |

## Evidence Commands

Real HttpClient, project-reference bootstrap simulation:

```bash
dotnet run --project demos/Qyl.RealHttpClientDemo/Qyl.RealHttpClientDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealHttpClientDemo/Qyl.RealHttpClientDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-httpclient-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-real-httpclient-aot/Qyl.RealHttpClientDemo
```

Real ASP.NET Core, project-reference bootstrap simulation:

```bash
dotnet run --project demos/Qyl.RealAspNetCoreDemo/Qyl.RealAspNetCoreDemo.csproj -c Release --no-build --no-launch-profile
dotnet publish demos/Qyl.RealAspNetCoreDemo/Qyl.RealAspNetCoreDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-aspnetcore-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-real-aspnetcore-aot/Qyl.RealAspNetCoreDemo
```

Real EFCore, project-reference bootstrap simulation:

```bash
dotnet ef dbcontext optimize --project demos/Qyl.RealEfCoreDemo/Qyl.RealEfCoreDemo.csproj --startup-project demos/Qyl.RealEfCoreDemo/Qyl.RealEfCoreDemo.csproj --context Qyl.RealEfCoreDemo.ProbeContext --output-dir CompiledModels --namespace Qyl.RealEfCoreDemo.CompiledModels --nativeaot
dotnet run --project demos/Qyl.RealEfCoreDemo/Qyl.RealEfCoreDemo.csproj -c Release --no-build /p:QylEfCoreUseCompiledModel=true
dotnet publish demos/Qyl.RealEfCoreDemo/Qyl.RealEfCoreDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-efcore-aot /p:PublishAot=true /p:InvariantGlobalization=true /p:QylEfCoreUseCompiledModel=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-efcore-aot/Qyl.RealEfCoreDemo
# NativeAOT binary passes. TreatWarningsAsErrors=false is intentional here because EFCore
# 10.0.8 still emits its own IL2026/IL3050/IL2104/IL3053/IL3002 warnings even on the
# compiled-model path.

dotnet pack src/Qyl.AutoInstrumentation/Qyl.AutoInstrumentation.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.DiagnosticListeners/Qyl.AutoInstrumentation.DiagnosticListeners.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.EntityFrameworkCore/Qyl.AutoInstrumentation.EntityFrameworkCore.csproj -c Release -o /tmp/qyl-pack
# A temp consumer with PackageReference=Qyl.AutoInstrumentation.EntityFrameworkCore and no qyl
# startup call restored from /tmp/qyl-pack and printed: PASS name=DB INSERT.
```

Real Grpc.Net.Client, project-reference bootstrap simulation:

```bash
dotnet run --project demos/Qyl.RealGrpcClientDemo/Qyl.RealGrpcClientDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealGrpcClientDemo/Qyl.RealGrpcClientDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-grpc-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-real-grpc-aot/Qyl.RealGrpcClientDemo

dotnet pack src/Qyl.AutoInstrumentation/Qyl.AutoInstrumentation.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.DiagnosticListeners/Qyl.AutoInstrumentation.DiagnosticListeners.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.Hosting/Qyl.AutoInstrumentation.Hosting.csproj -c Release -o /tmp/qyl-pack
# A temp consumer with PackageReference=Qyl.AutoInstrumentation.Hosting and no qyl startup call
# restored from /tmp/qyl-pack and printed: PASS name=qyl.LiveProbe/Collect.
```

Real Microsoft.Data.SqlClient, project-reference bootstrap simulation:

```bash
export QYL_SQL_PASSWORD='<strong local password>'
docker run --rm -d --platform linux/amd64 --name qyl-sqlclient-probe \
  -e ACCEPT_EULA=Y \
  -e MSSQL_SA_PASSWORD="$QYL_SQL_PASSWORD" \
  -p 11433:1433 \
  mcr.microsoft.com/mssql/server:2022-latest
export QYL_SQLCLIENT_CONNECTION_STRING="Server=127.0.0.1,11433;User ID=sa;Password=$QYL_SQL_PASSWORD;Initial Catalog=tempdb;Encrypt=True;TrustServerCertificate=True;Connect Timeout=5"

dotnet run --project demos/Qyl.RealSqlClientDemo/Qyl.RealSqlClientDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealSqlClientDemo/Qyl.RealSqlClientDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-sqlclient-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-sqlclient-aot/Qyl.RealSqlClientDemo
# Do not pass /p:InvariantGlobalization=true: Microsoft.Data.SqlClient throws
# NotSupportedException in globalization invariant mode. TreatWarningsAsErrors=false is intentional
# here because Microsoft.Data.SqlClient 7.0.1 emits IL2104/IL3053 warnings during NativeAOT publish.

dotnet pack src/Qyl.AutoInstrumentation/Qyl.AutoInstrumentation.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.DiagnosticListeners/Qyl.AutoInstrumentation.DiagnosticListeners.csproj -c Release -o /tmp/qyl-pack
dotnet pack src/Qyl.AutoInstrumentation.SqlClient/Qyl.AutoInstrumentation.SqlClient.csproj -c Release -o /tmp/qyl-pack
# A temp consumer with PackageReference=Qyl.AutoInstrumentation.SqlClient and no qyl startup call
# restored from /tmp/qyl-pack, published under NativeAOT, and printed:
# PASS name=SQL SELECT operation=SELECT server=127.0.0.1:11433.

docker rm -f qyl-sqlclient-probe
```

Real Confluent.Kafka, source-interceptor proof:

```bash
docker run -d --name qyl-kafka-probe -p 19092:19092 \
  -e KAFKA_NODE_ID=1 \
  -e KAFKA_PROCESS_ROLES=broker,controller \
  -e KAFKA_LISTENERS=PLAINTEXT://0.0.0.0:19092,CONTROLLER://0.0.0.0:9093 \
  -e KAFKA_ADVERTISED_LISTENERS=PLAINTEXT://127.0.0.1:19092 \
  -e KAFKA_CONTROLLER_LISTENER_NAMES=CONTROLLER \
  -e KAFKA_LISTENER_SECURITY_PROTOCOL_MAP=CONTROLLER:PLAINTEXT,PLAINTEXT:PLAINTEXT \
  -e KAFKA_CONTROLLER_QUORUM_VOTERS=1@localhost:9093 \
  -e KAFKA_OFFSETS_TOPIC_REPLICATION_FACTOR=1 \
  -e KAFKA_GROUP_INITIAL_REBALANCE_DELAY_MS=0 \
  apache/kafka:4.1.0
export QYL_KAFKA_BOOTSTRAP_SERVERS=127.0.0.1:19092

dotnet run --project demos/Qyl.RealKafkaDemo/Qyl.RealKafkaDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealKafkaDemo/Qyl.RealKafkaDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-kafka-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-kafka-aot/Qyl.RealKafkaDemo
# TreatWarningsAsErrors=false is intentional here because Confluent.Kafka 2.14.2 emits its own
# IL2104 trim warning. The demo csproj roots Confluent.Kafka for the trimmer because librdkafka
# delegate wiring reflects over the library's own NativeMethods members.
# Both runs printed Pass=true with 2 producer spans, >=2 consumer spans, and 1
# ProduceException`2 error span.

docker rm -f qyl-kafka-probe
```

Real RabbitMQ.Client, source-interceptor proof:

```bash
docker run -d --name qyl-rabbitmq-probe -p 5673:5672 rabbitmq:4
export QYL_RABBITMQ_URI=amqp://guest:guest@127.0.0.1:5673

dotnet run --project demos/Qyl.RealRabbitMqDemo/Qyl.RealRabbitMqDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealRabbitMqDemo/Qyl.RealRabbitMqDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-rabbitmq-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-rabbitmq-aot/Qyl.RealRabbitMqDemo
# Both runs printed Pass=true with 1 publish span and 1 AlreadyClosedException error span from
# a missing-exchange publish under publisher confirmations.

docker rm -f qyl-rabbitmq-probe
```

Real MongoDB.Driver, source-interceptor proof:

```bash
docker run -d --name qyl-mongodb-probe -p 27018:27017 mongo:8
export QYL_MONGODB_CONNECTION_STRING=mongodb://127.0.0.1:27018

dotnet run --project demos/Qyl.RealMongoDbDemo/Qyl.RealMongoDbDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealMongoDbDemo/Qyl.RealMongoDbDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-mongodb-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-mongodb-aot/Qyl.RealMongoDbDemo
# TreatWarningsAsErrors=false is intentional here because MongoDB.Driver/MongoDB.Bson 3.9.0 emit
# their own IL2104/IL3053 warnings. The demo csproj roots MongoDB.Bson and MongoDB.Driver for the
# trimmer; without the roots the NativeAOT binary hangs in driver initialization before any I/O.
# Both runs printed Pass=true with insert/count/delete spans and 1 MongoWriteException error span.

docker rm -f qyl-mongodb-probe
```

Real StackExchange.Redis, source-interceptor proof:

```bash
docker run -d --name qyl-redis-probe -p 16379:6379 redis:8
export QYL_REDIS_CONFIGURATION=127.0.0.1:16379

dotnet run --project demos/Qyl.RealRedisDemo/Qyl.RealRedisDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealRedisDemo/Qyl.RealRedisDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-redis-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-redis-aot/Qyl.RealRedisDemo
# Both runs printed Pass=true with SET/GET/DEL success spans and 1 RedisServerException error
# span from an unknown ExecuteAsync command. The AOT publish produced zero IL warnings.

docker rm -f qyl-redis-probe
```

Real Quartz, source-interceptor proof (in-process scheduler, no container):

```bash
dotnet run --project demos/Qyl.RealQuartzDemo/Qyl.RealQuartzDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealQuartzDemo/Qyl.RealQuartzDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-quartz-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-quartz-aot/Qyl.RealQuartzDemo
# TreatWarningsAsErrors=false is intentional here because Quartz 3.18.1 emits its own
# IL2104/IL3053 warnings. The demo csproj roots Quartz for the trimmer because Quartz
# instantiates its type-load helper and jobs through Activator.CreateInstance.
# Both runs printed Pass=true with 1 success span and 1 InvalidOperationException error span
# from source-visible IJob.Execute delegation inside a scheduler-fired job.
```

Real MassTransit, source-interceptor proof:

```bash
docker run -d --name qyl-rabbitmq-probe -p 5673:5672 rabbitmq:4
export QYL_RABBITMQ_URI=amqp://guest:guest@127.0.0.1:5673

dotnet run --project demos/Qyl.RealMassTransitDemo/Qyl.RealMassTransitDemo.csproj -c Release --no-build
dotnet publish demos/Qyl.RealMassTransitDemo/Qyl.RealMassTransitDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-real-masstransit-aot /p:PublishAot=true /p:TreatWarningsAsErrors=false
/tmp/qyl-real-masstransit-aot/Qyl.RealMassTransitDemo
# TreatWarningsAsErrors=false is intentional here because MassTransit 8.5.10 emits its own IL
# trim/AOT warnings. NativeAOT works because the demo chains a source-generated
# JsonSerializerContext into MassTransit's serializer options.
# Both runs printed Pass=true with publish/send success spans and 1 ArgumentException error
# span from MassTransit rejecting a namespace-less message type inside the intercepted call.

docker rm -f qyl-rabbitmq-probe
```

Real NServiceBus, source-interceptor proof (LearningTransport, no container, managed only):

```bash
dotnet run --project demos/Qyl.RealNServiceBusDemo/Qyl.RealNServiceBusDemo.csproj -c Release --no-build
# Printed Pass=true with publish/send success spans (including a real handler round-trip) and
# 1 error span from an unrouted send. NativeAOT is not run: NServiceBus 10 endpoint creation
# unconditionally constructs a Reflection.Emit proxy creator (MessageMapper ->
# ConcreteProxyCreator), which NativeAOT structurally cannot execute. That boundary belongs to
# NServiceBus, not qyl; the interceptor binding itself is AOT-clean.
```

Synthetic multi-domain semantic proof:

```bash
dotnet run --project demos/Qyl.LiveInstrumentationDemo/Qyl.LiveInstrumentationDemo.csproj -c Release --no-build -- --json /tmp/qyl-live-semantics.json --html /tmp/qyl-live-semantics.html
dotnet publish demos/Qyl.LiveInstrumentationDemo/Qyl.LiveInstrumentationDemo.csproj -c Release -r osx-arm64 --self-contained true -o /tmp/qyl-live-semantics-aot /p:PublishAot=true /p:InvariantGlobalization=true
/tmp/qyl-live-semantics-aot/Qyl.LiveInstrumentationDemo --json /tmp/qyl-live-semantics-aot.json --html /tmp/qyl-live-semantics-aot.html
```
