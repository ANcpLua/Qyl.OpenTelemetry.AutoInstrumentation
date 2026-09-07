# Qyl.Telemetry.AutoInstrumentation

Managed automatic instrumentation for .NET 10 applications, including NativeAOT consumers. It
works through compiler-generated Roslyn interceptors, `ActivitySource` subscription, public
`DiagnosticListener` hooks, build assets and module-initializer bootstrap. There is no CLR
profiler, no startup hook, no ReJIT, no runtime IL rewriting and no dynamic plugin loading.

Roslyn interceptors are supported by this repository's .NET SDK 10.0.400. See the official
[`interceptors.md`](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md)
contract.

Every attribute key, attribute value and telemetry scope name this package writes is a generated
constant from `Qyl.Telemetry.SemanticConventions` 9.3.0. The instrumentation writes those constants
and nothing else: it never renames, drops or coerces what a library emitted. Deprecated keys and
vendor keys travel as the library wrote them and the qyl collector rewrites them; the live check
below is what proves it.

## Packages

| Package | What it does |
| --- | --- |
| `Qyl.Telemetry.Hosting` | One-line onboarding. `builder.AddQyl()` wires the OpenTelemetry SDK, subscribes the native `ActivitySource` table, adds the two processors, registers the meter inventory and exports OTLP with collector discovery. |
| `Qyl.Telemetry.AutoInstrumentation` | The core runtime: the single qyl `ActivitySource`, the interceptor declarations and helper bodies, the options surface, the compiler-facing ABI, the build assets and the source generator. |
| `Qyl.Telemetry.AutoInstrumentation.Hosting` | Process bootstrap: a `[ModuleInitializer]` activates the qyl listeners when the assembly loads, and `AddQylAutoInstrumentation()` wires them explicitly. |
| `Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners` | The base `DiagnosticListener` subscriber and the shared semantics helpers the EF Core and SqlClient packages build on. |
| `Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore` | EF Core `DiagnosticSource` instrumentation. |
| `Qyl.Telemetry.AutoInstrumentation.SqlClient` | `Microsoft.Data.SqlClient` `DiagnosticSource` instrumentation. |

Add the package that owns the integration you need; the supported zero-configuration consumer path
is a `PackageReference`, and build and analyzer assets flow through NuGet.

```bash
dotnet add package Qyl.Telemetry.Hosting --version 15.0.0
```

```csharp
using Qyl;

builder.AddQyl();
```

That consumer publishes NativeAOT warning-free — the package contributes no trim, AOT or analyzer
warning to the publish:

```bash
dotnet publish -r <rid> -p:PublishAot=true
```

`AddQyl()` activates the qyl listeners; registers the qyl `ActivitySource`, the framework-native
`Microsoft.AspNetCore` and `System.Net.Http` sources, the version-pinned GenAI, MCP, Azure SDK and
CoreWCF sources and every enabled row of the native-source table; adds the ASP.NET Core enrichment
middleware, `QylNativeSpanProcessor` and `QylSessionSpanProcessor`; registers the meter inventory;
and exports traces, metrics and logs over OTLP — to `OTEL_EXPORTER_OTLP_ENDPOINT` when it is set, otherwise to
`QYL_ENDPOINT` when that is set, otherwise to a qyl collector discovered on `localhost` or the host
`qyl` at 4318/4317. `QYL_ENDPOINT` names a collector for the qyl exporters alone, where the
standard variable would redirect every OTLP exporter in the process.

There is no per-integration enable step: `AddQyl()` subscribes every enabled row of the
native-source table below, and every `[QylIntercept]` declaration is compiled into the consumer's
build by the package's own analyzer and build assets. An integration is on unless the
`OTEL_DOTNET_AUTO_*_INSTRUMENTATION_ENABLED` toggle for its instrumentation id turns it off —
those default to enabled, and they are the only switch.

An application that wires the SDK itself references
`Qyl.Telemetry.AutoInstrumentation.Hosting` alongside `OpenTelemetry.Extensions.Hosting` and
`OpenTelemetry.Exporter.OpenTelemetryProtocol`, and registers the sources by hand:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-service"))
    .WithTracing(t => t
        .AddSource("Qyl.Telemetry.AutoInstrumentation")
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
```

The instrumentation packages ship no exporter of their own, and a manually wired application that
subscribes a native source qyl also owns exports the same operation twice.

## See a span without a collector

No package here ships a console exporter, and neither does the OpenTelemetry SDK
`Qyl.Telemetry.Hosting` pulls in. Add OpenTelemetry's own, hand it to the `ConfigureTracing` hook
`AddQyl()` exposes, and run:

```bash
dotnet add package OpenTelemetry.Exporter.Console
```

```csharp
builder.AddQyl(o => o.ConfigureTracing = t => t.AddConsoleExporter());
```

```bash
dotnet run
```

The console exporter writes each activity as it ends, so nothing has to be listening. One
`HttpClient` call prints exactly one client span, the framework's, under the `System.Net.Http`
scope and carrying the `qyl.instrumentation.domain` the processor stamped on it. The same span
reaches `weaver registry live-check`, which listens for
OTLP/gRPC on 4317 and prints every span it receives, when `OTEL_EXPORTER_OTLP_ENDPOINT` points at it
— judged against the qyl registry, as the [live check](#live-check) below runs it, because the
upstream registry alone does not declare `qyl.instrumentation.domain`.

## How a library gets instrumented

One question decides the mechanism:

> Does `AddSource("<name>")` alone deliver spans — without a contrib package and without a
> `DiagnosticListener` adapter?
>
> - **Yes** → the source name goes in `QylTelemetrySources`, `AddQyl()` subscribes it, and
>   `QylNativeSpanProcessor` stamps the qyl attributes onto the library's own spans.
> - **No** → a Roslyn source interceptor in `Qyl.Telemetry.AutoInstrumentation`.

A library that emits only `DiagnosticSource`/`DiagnosticListener` events counts as No: an adapter is
a contrib package by another name. A library whose `ActivitySource` stays silent until the consumer
opts in *in their own code* still takes source + processor — qyl does not call the opt-in on the
application's behalf, because an interceptor that did would be the mechanism this rule removes.
No source name is typed by hand anywhere: `QylTelemetrySources` reads
`QylTelemetryNames.VendorActivitySources`, so a name missing from the pin is a
semantic-convention release blocker rather than a string literal.

### Subscribed native sources

One row per entry of `QylTelemetrySources.NativeSourceRows`. The row carries the source name, the
instrumentation id whose toggle gates it, and the `qyl.instrumentation.domain` value the processor
stamps. `Azure.*` matches by prefix — the Azure SDK publishes one source per service — and every
other row is an ordinal exact match.

| Library | Pinned | `ActivitySource` | Instrumentation id | Domain |
| --- | --- | --- | --- | --- |
| Azure SDK | `Azure.Storage.Blobs` `12.29.2` | `Azure.*` | `AZURE` | `azure.sdk` |
| CoreWCF | `CoreWCF.Http` `1.9.1` | `CoreWCF.Primitives` | `WCFCORE` | none — see below |
| HttpClient | `System.Net.Http` (BCL) | `System.Net.Http` | `HTTPCLIENT` | `http.client` |
| MySql.Data | `26.7.0` | `connector-net` | `MYSQLDATA` | `db.client` |
| Elastic.Transport, Elastic.Clients.Elasticsearch | `1.0.0`, `9.5.1` | `Elastic.Transport` | `ELASTICTRANSPORT` | `elastic.transport` |
| GraphQL.NET | `8.8.5` | `GraphQL` | `GRAPHQL` | `graphql` |
| Grpc.Net.Client | `2.83.0` | `Grpc.Net.Client` | `GRPCNETCLIENT` | `rpc.grpc` |
| MassTransit | `[8.5.10,9.0.0)` | `MassTransit` | `MASSTRANSIT` | `messaging.masstransit` |
| MongoDB.Driver | `3.11.1` | `MongoDB.Driver` | `MONGODB` | `db.mongodb` |
| MySqlConnector | `2.6.2` | `MySqlConnector` | `MYSQLCONNECTOR` | `db.client` |
| Npgsql | `10.0.3` | `Npgsql` | `NPGSQL` | `db.client` |
| NServiceBus | `10.2.9` | `NServiceBus.Core` | `NSERVICEBUS` | `messaging.nservicebus` |
| Oracle.ManagedDataAccess.Core | `23.26.300` | `Oracle.ManagedDataAccess.Core` | `ORACLEMDA` | `db.client` |
| Quartz.NET | `4.0.0` | `Quartz` | `QUARTZ` | `job.quartz` |
| RabbitMQ.Client | `7.2.2` | `RabbitMQ.Client.Publisher` | `RABBITMQ` | `messaging.rabbitmq` |
| RabbitMQ.Client | `7.2.2` | `RabbitMQ.Client.Subscriber` | `RABBITMQ` | `messaging.rabbitmq` |

The Elasticsearch client owns no `ActivitySource`; it enriches Elastic.Transport's, so the two
integrations share one row and one domain. CoreWCF's row carries no domain: those spans are the WCF
*server* side and the registry publishes no instrumentation-domain value for it —
`rpc.wcf.client` belongs to the intercepted client. That is a semantic-convention gap, not a value
to invent here, so the row exists for its `AddSource` call alone.

`QylTelemetrySources` also subscribes sources that need no processor row, because the library
already writes the semantic conventions and qyl adds nothing: `System.Net.Http`,
`Experimental.Microsoft.Extensions.AI`, `Experimental.Microsoft.Agents.AI`,
`Microsoft.Agents.AI.Workflows` and `Experimental.ModelContextProtocol`. Three of the AI paths need
the consumer's own opt-in —
`chatClient.AsBuilder().UseOpenTelemetry().Build()` for `Microsoft.Extensions.AI` 10.9.0,
`agent.AsBuilder().UseOpenTelemetry().Build()` for `Microsoft.Agents.AI` 1.20.0, and
`WorkflowBuilder.WithOpenTelemetry()` for `Microsoft.Agents.AI.Workflows` 1.20.0. `ModelContextProtocol`
2.2.0 and CoreWCF emit without one. MCP *metrics* are deliberately not registered: the official
instruments carry dynamic tool and resource names as dimensions, which conflicts with qyl's
bounded-cardinality policy.

### Roslyn interceptors

One row per `[QylIntercept]` declaration in `src/Qyl.Telemetry.AutoInstrumentation`.

| Integration | Pinned | Intercepted receiver | Instrumentation id | Domain |
| --- | --- | --- | --- | --- |
| ADO.NET | `System.Data.Common` | `System.Data.Common.DbCommand` | `ADONET`, fanned out to `SQLCLIENT` and `SQLITE` by the receiver's namespace | `db.client` |
| Confluent.Kafka | `2.15.0` | `Confluent.Kafka.IProducer<TKey, TValue>` and `IConsumer<TKey, TValue>` | `KAFKA` | `messaging.kafka` |
| StackExchange.Redis | `3.1.31` | `StackExchange.Redis.IDatabaseAsync` | `STACKEXCHANGEREDIS` | `db.redis` |
| WCF client | `System.ServiceModel.Primitives` `10.0.652802` | `System.ServiceModel.ClientBase<TChannel>` | `WCFCLIENT` | `rpc.wcf.client` |

`System.Data.Common`, `Microsoft.Data.SqlClient` 7.0.2, `Microsoft.Data.Sqlite` 10.0.11,
`Confluent.Kafka` 2.15.0, `StackExchange.Redis` 3.1.31 and `System.ServiceModel.*` declare no
`ActivitySource` at the pinned version, so the rule puts them here. There is no longer an
integration in this table whose library owns a native source: the four database providers that did
— Npgsql, MySqlConnector, MySql.Data and Oracle ODP.NET — moved to the table above in 15.0.0, and
the ADO.NET declaration names them in `NativeSourceReceivers`, so a call site on one of those
receivers emits no interceptor and reports no `QYL1001`. It is not a shape mismatch; it is a lane
that belongs to the library.

### The libraries whose telemetry the consumer turns on

Three rows of the native-source table stay silent, or stay poorer, until the application makes a
call in its own code. qyl never makes that call: injecting a consumer's opt-in would be the
interception this package family exists to remove, and it would turn on telemetry the application
did not ask for. The source generator reports `QYL1002` when a compilation uses the library and
never makes the call.

| Library | The call | Without it |
| --- | --- | --- |
| GraphQL.NET | `IGraphQLBuilder.UseTelemetry()` | the `GraphQL` `ActivitySource` stays silent: no spans at all |
| Oracle ODP.NET | `TracerProviderBuilder.AddOracleDataProviderInstrumentation()` (package `Oracle.ManagedDataAccess.OpenTelemetry`) | the command spans still arrive, carrying only `db.system`, `db.odp.roundtrip.count`, `db.odp.roundtrip.duration` and `db.response.returned_rows`; `db.name`, `db.user`, `db.statement`, `server.address`, `server.port`, `db.odp.connection.id`, `db.odp.sql_id` and exception recording stay off |
| Microsoft.Extensions.AI, Microsoft.Agents.AI, Workflows | `UseOpenTelemetry()` / `WithOpenTelemetry()` | the GenAI sources stay silent |

**MySql.Data needs nothing.** `MySql.Data.OpenTelemetry`'s `AddConnectorNet()` is
`builder.AddSource("connector-net")` and nothing else, which is exactly what `AddQyl()` already
does, so there is no opt-in for a consumer to add and no `QYL1002` row for it.

**MySql.Data sends the statement text unmasked.** Its `db.statement` carries the full command text
on every span, unconditionally, with no option to turn it off — `OTEL_SEMCONV_STABILITY_OPT_IN` is a
no-op for it and qyl does not strip what a library wrote. The value leaves the process; masking it
is the collector's job.

### ASP.NET Core: one server span, and it is the framework's

ASP.NET Core is the third shape. Its `Microsoft.AspNetCore` source is native, so `AddQyl()`
subscribes it and `Microsoft.AspNetCore.Hosting.HttpRequestIn` is the one HTTP SERVER span of a
request — qyl starts none of its own. But unlike every library in the table above, the runtime
creates that activity *empty*: measured on .NET 10.0.11 it carries no tag at all and keeps its raw
operation name. So enrichment is not a processor row here. `AddQylAspNetCoreInstrumentation()`,
which `AddQyl()` calls, registers an `IStartupFilter` whose middleware writes onto the hosting
activity, from the `HttpContext`: `qyl.instrumentation.domain`, `http.request.method`, `url.scheme`,
`url.path`, `url.query` under the redaction control, `http.route` and the `{method} {route}` span
name after routing, `http.response.status_code`, `error.type` on failure, and the configured request
and response headers. Each write fills only what is absent, so a runtime that starts setting these
itself takes them over without a change here.

Two integrations use a framework's public `DiagnosticListener` hook instead of either mechanism:
EF Core through `.EntityFrameworkCore` and `Microsoft.Data.SqlClient` through `.SqlClient`. Neither
library declares an `ActivitySource`, so a listener is the only managed hook there is. No signal has
two qyl lanes any more, which is why there is no arbitration between them: 15.0.0 deleted the last
pair, HttpClient's.

### Toggles

Every row above, native or intercepted, is gated per signal by its instrumentation id:
`OTEL_DOTNET_AUTO_{TRACES|METRICS|LOGS}_<ID>_INSTRUMENTATION_ENABLED`, with the per-signal
`OTEL_DOTNET_AUTO_{TRACES|METRICS|LOGS}_INSTRUMENTATION_ENABLED` and the global
`OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED` taking precedence. `AddQyl()` reads the same options, so
a disabled id contributes neither an `AddSource` call nor a processor row. Query-text capture is
opt-in per provider — `OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT`,
`OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT`,
`OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT`, `OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT` — and
so is header capture on the ASP.NET Core, HTTP and gRPC-client lanes.

qyl never force-registers a library's own `Meter`. A consumer that wants `Npgsql`'s or
`NServiceBus.Core`'s native instruments exported registers them through
`OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES` or `QylSdkOptions.AdditionalMeters`.

The full 66-row contract, with every environment control and instrumentation option, its evidence
level and its authoritative source, is the generated
[coverage matrix](docs/coverage-matrix.md).

## The qyl attributes

`qyl.instrumentation.domain` is the only qyl-owned attribute written onto a span.
`QylNativeSpanProcessor` writes it onto the spans of the native-source table, and the ASP.NET Core
middleware writes it onto the hosting activity, which is the one native span whose own library
writes nothing. Everything else on a native span is the library's own. The domain is what the qyl collector's dashboard classifies on,
together with the semantic-convention keys the library emits, so a span that reaches the collector
without it is unclassifiable no matter what it is called.

Its value set is registry-owned (`QylAttributes.InstrumentationDomainValues`): `aspnetcore.server`,
`azure.sdk`, `db.client`, `db.efcore`, `db.mongodb`, `db.redis`, `db.sqlclient`,
`elastic.transport`, `graphql`, `http.client`, `job.quartz`, `messaging.kafka`,
`messaging.masstransit`, `messaging.nservicebus`, `messaging.rabbitmq`, `rpc.grpc` and
`rpc.wcf.client`.

`QylSessionSpanProcessor` copies `session.id` from the nearest tagged in-process ancestor onto
descendant spans that do not carry one. Remote parents and unrelated trace branches propagate
nothing, and the copy happens on end, the last moment the ancestor's tag can be observed.
`demos/Qyl.RealSessionPropagationDemo` and `tools/verify-real-session-propagation-demo.py` hold
that to three assertions across two real operating-system processes: an in-process descendant
inherits the value, a descendant that already carries one keeps its own, and the server span in
the second process carries none — although `traceparent` arrived, the trace is the same one, and
the session itself reached that span in baggage.

`session.id` is a **span tag and nothing else**. Nothing in `Qyl.Telemetry.Hosting` puts it on the
wire: an outgoing request carries the trace context and whatever baggage the application itself put
on `Activity.Current`, which is what `System.Net.Http` propagates. A tag is not baggage, so the
next process sees the session only if the application put it in baggage on purpose.

qyl's own spans and instruments carry the registry-owned scope names
`Qyl.Telemetry.AutoInstrumentation` (`ActivitySource`) and
`Qyl.Telemetry.AutoInstrumentation.Database` (`Meter`, carrying `db.client.operation.duration`).
Mirror them in `AddSource(...)`, `AddMeter(...)` or
`OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES`. The generated-code ABI anchor is
`QylGeneratedCodeAbi.V17` in the `Qyl.Telemetry.AutoInstrumentation.GeneratedCode` namespace and
tracks the package major, so a generated interceptor from another major fails to compile rather
than binding to this runtime.

## Analyzers and generator diagnostics

The source generator reports two diagnostics of its own:

| Id | Severity | Reported when |
| --- | --- | --- |
| `QYL1001` | Info | A call site names a declared integration's receiver type and method but its signature does not fit the declared shape, typically because the library changed the signature in a new major. No interceptor is emitted for that call, so it produces no qyl telemetry. Update the declaration or pin the library to a version the shape describes. |
| `QYL1002` | Info | The compilation uses a library whose native `ActivitySource` qyl subscribes but never makes the call that library needs before it emits, or before it emits in full — see [the opt-in table](#the-libraries-whose-telemetry-the-consumer-turns-on). The message names the call and what is lost without it. |

Skipping in silence would hide the loss of instrumentation, and emitting an interceptor with a
mismatched signature would break the consumer's build; the diagnostic is the third option.

`Qyl.Telemetry.AutoInstrumentation` also consumes
`Qyl.Telemetry.SemanticConventions.Analyzers` 9.3.0 with `PrivateAssets="all"`, so the `QYL0xxx`
rules run over this repository's own sources and ship to no consumer. `Directory.Build.props` sets
`OtelSemConvInstrumentationLibrary=true`: this is an instrumentation library that version-locks with
the incubating tier on purpose, so `QYL0008` ("copy incubating constants locally") does not apply.
The rules themselves are owned and documented by the semantic-conventions repository.

## Demos and verifiers

Thirty demo applications under `demos/` are the runtime evidence. Each has a verifier under
`tools/` that builds it, publishes it NativeAOT where the integration supports it, starts any
container it needs, runs it and asserts the spans and metrics it emitted:

```bash
python3 tools/verify-real-quartz-demo.py
```

Ten verifiers need Docker, and each names its image in an overridable variable: `masstransit`
and `rabbitmq` use `QYL_RABBITMQ_IMAGE` (`rabbitmq:4.1-alpine`), `kafka` uses `QYL_KAFKA_IMAGE`
(`apache/kafka:4.1.0`), `mongodb` uses `QYL_MONGODB_IMAGE` (`mongo:8-noble`), `redis` uses
`QYL_REDIS_IMAGE` (`redis:8-alpine`), `npgsql` uses `QYL_POSTGRES_IMAGE` (`postgres:18-alpine`),
`mysqlconnector` and `mysqldata` use `QYL_MYSQL_IMAGE` (`mysql:9`), `oraclemda` uses
`QYL_ORACLE_IMAGE` (`gvenzl/oracle-free:23-slim-faststart`), and `sqlclient` uses
`QYL_SQLSERVER_IMAGE` (`mcr.microsoft.com/mssql/server:2022-latest`, which ships no arm64 image, so
that lane runs on x64 in CI). The four database verifiers gained their containers in 15.0.0: a
native source only emits against a real server, where the old interceptor demos proved themselves
against a connectionless exception.

The complete local gate runs every verifier in order — contract invariants, release and demo
builds, package layout, public API baselines, generator snapshots, the NativeAOT publish matrix,
all thirty demos, the live check, the smoke test and the published-consumer evidence:

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py
```

`--list` prints the command names, and `--only NAME` / `--skip NAME` select from them. The two
verifiers take different vocabularies and they do not overlap: this gate's names are the ones
`--list` prints, so a skip is quoted gate names —

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py --skip "real mongodb demo,live check"
```

— while `verify-live-check.py` names its demo lanes, one per row of the native-source table:

```bash
QYL_SEMCONV_REGISTRY=../Qyl.OpenTelemetry.SemanticConventions python3 tools/verify-live-check.py --skip mongodb
```

## Live check

`tools/verify-live-check.py` runs `weaver registry live-check` as an OTLP/gRPC listener on 4317 and
points nine demo lanes at it, so what the instrumentation actually emitted is judged against the
pinned semantic-convention registry rather than against an assertion written in this repository.
The lanes are one per row of the native-source table — `azure`, `corewcf`, `elastictransport`,
`elasticsearch`, `masstransit`, `mongodb`, `nservicebus`, `quartz`, `rabbitmq` — with RabbitMQ's two
source names on one lane, and Elastic.Transport and Elasticsearch on a lane each because the two
clients put different attributes on the shared source. Weaver judges every span, metric and resource
it receives: the library's keys, the vendor keys it passes through and the
`qyl.instrumentation.domain` the processor stamped.

`--fail-on violation` is the threshold and there is no allowlist in the gate. A finding is closed by
changing what the instrumentation writes or by declaring the key in the registry, never by waving it
through. Against the `v9.3.0` registry the lanes report zero violations.

How a finding is *levelled* is the registry's decision, and it takes two flags that must travel
together:

- `--config <registry>/.weaver.toml` drops weaver's built-in `deprecated`, `type_mismatch` and
  `undefined_enum_variant` findings by id. Those advisors are compiled into the binary and emit at a
  level no policy can lower.
- `--advice-policies <registry>/policies/live_check_advice` re-issues them at the level the registry
  chose: an open enum carrying `_OTHER` is information, a renamed or obsoleted key a library still
  emits is an improvement, a type mismatch whose value parses is an improvement.

Passing only the second changes nothing for those three advisors, and an unreadable path fails
silently, so the gate checks for both halves and refuses to run without them.

The registry is a checkout of
[Qyl.OpenTelemetry.SemanticConventions](https://github.com/ANcpLua/Qyl.OpenTelemetry.SemanticConventions),
named by `QYL_SEMCONV_REGISTRY`, with `scripts/fetch-core.sh` already run so `registry/manifest.yaml`
can resolve its filtered core dependency. The published NuGet packages carry the generated C#, not
the registry itself, which is why the gate needs the source repository:

```bash
QYL_SEMCONV_REGISTRY=../Qyl.OpenTelemetry.SemanticConventions python3 tools/verify-live-check.py
```

`--skip LANE[,LANE...]` skips a lane whose container this machine cannot run, and the skip is named
in the output as `live-check-partial-ok`. On macOS under OrbStack the MongoDB lane is that lane:
OrbStack's VM runs a 7.x kernel, and MongoDB 8 refuses to start on Linux 6.19 or newer
([SERVER-121912](https://jira.mongodb.org/browse/SERVER-121912)) — the container exits before it
listens, so the demo cannot connect. Run the other eight locally and let CI cover MongoDB:

```bash
QYL_SEMCONV_REGISTRY=../Qyl.OpenTelemetry.SemanticConventions python3 tools/verify-live-check.py --skip mongodb
```

One gap in coverage is recorded rather than hidden: **`RabbitMQ.Client.Subscriber` has no consuming
demo.** `AddQyl()` subscribes both RabbitMQ source names, but the RabbitMQ lane only publishes, so
the subscriber source emits nothing and its `deliver` and `fetch` spans are unjudged by this gate.

## Release

The version lives in `Directory.Build.props` `<Version>`, and the `Qyl.Telemetry.*` family ships as
one line at one version. Pushing the tag `v<version>`, which must equal that version at that commit,
is the act that publishes: `nuget-publish.yml` runs the full gate set, packs, publishes through NuGet
trusted publishing, proves the indexed packages restore into clean managed and NativeAOT consumers,
and only then creates the GitHub release. A push to `main` builds and verifies through the sibling
workflows and never publishes.

The package major is the compile-time ABI rather than the product version, which is why it runs
ahead of the rest of qyl: a `15.x` package pairs with `QylGeneratedCodeAbi.V15` and nothing else.

Two files name the semantic-convention release and they must agree: the
`Qyl.Telemetry.SemanticConventions` `PackageVersion` in `Directory.Packages.props`, whose generated
constants the code compiles against, and `SEMCONV_REF` in `.github/workflows/live-check.yml`, the
registry tag the live check judges against. `verify-live-check.py` compares the pin against the
checkout's `VersionPrefix` and fails when they are different releases — judging spans against a
registry other than the one that produced the constants is the drift that would otherwise look like
a green gate.

## Limitations

- Only source-visible call sites can be intercepted. Calls hidden in compiled dependencies,
  reflection or dynamic dispatch need a public runtime hook or remain unsupported.
- Some integrations are managed-only because the instrumented library requires runtime code
  generation. The NativeAOT boundary is this compile-time/managed substrate; it claims no parity
  with the CLR-profiler OpenTelemetry .NET automatic instrumentor and does not imply that every
  third-party library publishes warning-free under NativeAOT.
- Query text and other sensitive or high-cardinality values remain opt-in or redacted according to
  the package options and the upstream OpenTelemetry controls.
- Generic HTTP header capture never records the reserved `Mcp-Param-*` namespace. Those headers
  mirror MCP tool arguments; capturing argument content belongs to an MCP-specific, explicitly
  enabled policy rather than to the HTTP layer.
- Generator snapshots prove emitted source shape; protocol interoperability requires a real OTLP
  receiver and structural decoding of the official protobuf messages.
- MassTransit 9 is commercially licensed and is neither referenced nor redistributed here; the demo
  pin is the Apache-2.0 range `[8.5.10,9.0.0)`.

## License

Apache-2.0
