# Design

## How a library gets instrumented

qyl has exactly two mechanisms, and one question decides which a library gets:

> **Does `AddSource("<name>")` alone deliver spans, without a contrib package and without a
> `DiagnosticListener` adapter?**
>
> - **Yes** → `AddSource` plus one processor in `Qyl.Telemetry.Hosting` that stamps the qyl
>   attributes onto the library's own spans. No interceptor.
> - **No** → Roslyn source interceptor.
>
> A library that only emits `DiagnosticSource`/`DiagnosticListener` events counts as **No**: an
> adapter is a contrib package by another name.

There is a third bucket. A library may own an `ActivitySource` that stays silent until the consumer
opts in **in their own code** (`UseTelemetry()` on GraphQL.NET's builder, `AddInstrumentation()` on
HotChocolate, a driver setting on MongoDB). Those still take **source + processor** for the spans.
qyl does not inject the opt-in — an interceptor that called it would be the mechanism this rule
exists to remove. The opt-in is documented in the README and checked by an analyzer diagnostic, in
the pattern of the existing `QYL0101`-style rules in the Analyzers package.

A library whose version floor for the native `ActivitySource` is not proven **stays an interceptor**.
The floor is part of the evidence, not a detail: subscribing to a source that does not exist in the
consumer's pinned version yields silence.

`docs/contracts/otel-dotnet-auto-60.upstream.yaml` is a **secondary** source for this decision.
Upstream files reflection-based contrib packages under `source` too, so its `instrumentation_types`
answers a different question than the one above. The primary evidence is the library itself: the
type that declares the `ActivitySource`, or the vendor's own documentation.

The criterion is about mechanism, not preference. A native `ActivitySource` means the library
already produces spans a consumer can subscribe to; interception on top of that would duplicate
them. Interception exists for libraries that produce nothing on their own.

### Why not one processor per library

One processor in `Qyl.Telemetry.Hosting`, driven by a table from source name to
`(component, domain, instrumentation id)`. Eight copies of `QylAzureSpanProcessor` differing only in
a string constant is the duplication this repository deletes on sight.

## Audit

Every source name below was read from the type that declares it in the library's source at the
pinned version, with vendor documentation as corroboration.

| Library | Pinned | `AddSource` alone delivers spans? | Exact source name | Where the name was found | Mechanism after 13.0.0 |
| --- | --- | --- | --- | --- | --- |
| MassTransit | `8.5.10` | Yes | `MassTransit` | `MassTransit.Logging.DiagnosticHeaders.DefaultListenerName` | source + processor |
| Elastic.Transport | `1.0.0` | Yes | `Elastic.Transport` | `Elastic.Transport.Diagnostics.OpenTelemetry.ElasticTransportActivitySourceName` | source + processor |
| Elastic.Clients.Elasticsearch | `9.5.1` | Yes, via the transport | `Elastic.Transport` | client owns no source; enriches the transport's spans | source + processor |
| Quartz.NET | `4.0.0` | Yes | `Quartz` | `Quartz.Diagnostics.QuartzInstrumentation.ActivitySourceName` | source + processor |
| MongoDB.Driver | `3.11.1` | Yes | `MongoDB.Driver` | `MongoDB.Driver.MongoTelemetry.ActivitySourceName` | source + processor |
| NServiceBus | `10.2.9` | Yes | `NServiceBus.Core` | `NServiceBus.Core/OpenTelemetry/Tracing/ActivitySources.cs` | source + processor |
| RabbitMQ.Client | `7.2.2` | Yes | `RabbitMQ.Client.Publisher`, `RabbitMQ.Client.Subscriber` | `RabbitMQActivitySource.PublisherSourceName` / `.SubscriberSourceName` | source + processor |
| GraphQL | `8.8.5` | **No** — needs `UseTelemetry()` on the app's `IGraphQLBuilder` | `GraphQL` | `GraphQL.Telemetry.GraphQLTelemetryProvider.SourceName` | interceptor |
| Confluent.Kafka | `2.15.0` | **No** — no native source at all | n/a | contrib wraps `ProducerBuilder`/`ConsumerBuilder` | interceptor |

Documentation used: MassTransit <https://masstransit.massient.com/documentation/configuration/observability>;
Quartz <https://www.quartz-scheduler.net/documentation/quartz-4.x/packages/opentelemetry-integration.html>;
NServiceBus <https://docs.particular.net/nservicebus/operations/opentelemetry?version=core_10>;
Kafka contrib <https://www.nuget.org/packages/OpenTelemetry.Instrumentation.ConfluentKafka>.
Elastic, MongoDB and RabbitMQ publish no .NET OpenTelemetry documentation page; those rows rest on
the declaring type at the pinned tag.

### Version floors

The floor is the library version at which the native `ActivitySource` first exists. Below it, the
source name resolves to nothing.

| Library | Floor | How the floor was established | Confidence |
| --- | --- | --- | --- |
| MassTransit | `8.0.0` | OTel contrib deprecation README states v8.0.0 and later have built-in `ActivitySource` support | stated by upstream |
| RabbitMQ.Client | `7.0.0` | CHANGELOG, PR #1261, first shipped in `7.0.0-alpha.3`; 6.x has none | repository changelog |
| Quartz.NET | `4.0.0` | `Quartz.Diagnostics.QuartzInstrumentation.ActivitySourceName` at tag `v4.0.0`; 3.x used `DiagnosticListener`, so 3.x is bucket "no native" | declaring type at tag |
| MongoDB.Driver | `3.7.0` | release notes "Adds support for OpenTelemetry tracing" plus tag bisect (`MongoTelemetry.cs` absent at `v3.6.0`, present at `v3.7.0`) | bisect + release note |
| NServiceBus | `8.0` | documentation states OpenTelemetry support since v8; enabled by default from v10 | vendor documentation |
| Elastic.Transport | `8.10.0` of the client stack | name present in `Elastic.Transport` `1.0.0`; the pre-8.10 name is **unverified** | partial |
| GraphQL | `7.3.0` | tag bisect (absent `7.2.0`, present `7.3.0`); no changelog entry found | bisect only |

**Quartz 4 re-check, as asked:** `Quartz.Diagnostics.QuartzInstrumentation.ActivitySourceName` is a
real `ActivitySource`, not a `DiagnosticSource`. Quartz 3.x used `DiagnosticListener`, which is why
`OpenTelemetry.Instrumentation.Quartz` "produces nothing against 4.0". The floor is therefore exactly
the major this repository pins.

### Rows that carry a caveat

- **Quartz 4** qualifies mechanically, but its native span carries vendor `quartz.*` attributes and
  `error.type` — not messaging or database semantic conventions. Migrating it changes what a
  consumer receives, not only where it comes from.
- **NServiceBus** publishes no attribute list, and its documentation teaches the wildcard
  `NServiceBus.*` rather than the literal source name. Subscribe to the wildcard; do not treat
  `NServiceBus.Core` as contract.
- **Elasticsearch before 8.10.0** used a different source name. No Elastic page documents it and the
  claim is unverified. It does not affect the pinned version, which owns no source of its own.
- **RabbitMQ.Client** exposes process-wide mutable statics (`RabbitMQActivitySource.ContextInjector`,
  `.UseRoutingKeyAsOperationName`) that change propagation and span names. A processor shares that
  state with application code.

## Audit of the nine remaining interceptors

Audited 2026-09-04, one question each: does `AddSource("<name>")` alone deliver spans, without a
contrib package and without a `DiagnosticListener` adapter? The finding column names what was read —
the declaring type at a public tag, the shipped assembly of the pinned version, or the vendor's own
documentation. "None" in the opt-in column means the source emits with a listener attached and
nothing else.

| Integration | Pinned | Native source? | Exact name | Floor | Finding | Bucket | Opt-in? | Vendor / non-stable keys emitted |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| ADONET | `System.Data.Common`, `Microsoft.NETCore.App` `10.0.11` | No | n/a | n/a | String and metadata scan of `shared/Microsoft.NETCore.App/10.0.11/System.Data.Common.dll`: zero `ActivitySource`, zero `DiagnosticSource`/`DiagnosticListener`. `DbCommand` is abstract and emits nothing. | no native source | n/a | n/a |
| SQLCLIENT | `Microsoft.Data.SqlClient` `7.0.2` | No | n/a | n/a | `grep -rn ActivitySource` over `src/` at tag `v7.0.2`: zero hits. The only telemetry entry point is `SqlDiagnosticListener : DiagnosticListener` constructed as `base("SqlClientDiagnosticListener")`, `src/Microsoft.Data.SqlClient/src/Microsoft/Data/SqlClient/Diagnostics/SqlDiagnosticListener.cs:18`. `DiagnosticSource` only counts as No. | `DiagnosticSource` only | n/a | n/a |
| SQLITE | `Microsoft.Data.Sqlite` `10.0.11` | No | n/a | n/a | `grep -rn ActivitySource` over `src/Microsoft.Data.Sqlite.Core/` at `dotnet/efcore` tag `v10.0.11`: zero hits; no file in that tree matches `activity`, `telemetry` or `diagnostic`. | no native source | n/a | n/a |
| NPGSQL | `Npgsql` `10.0.3` | **Yes** | `Npgsql` | `6.0.0` | `static readonly ActivitySource Source = new("Npgsql", GetLibraryVersion());` — `src/Npgsql/NpgsqlActivitySource.cs:15` at tag `v10.0.3`. Floor by bisect: the same file declares `Source = new("Npgsql", version)` at tag `v6.0.0`; at `v5.0.0` the path is absent and the `v5.0.0` tree contains no `*Activity*` file under `src/Npgsql/`. | native | None — the only gate is `Source.HasListeners()`; `NpgsqlTracingOptionsBuilder` filters and enriches, it does not enable | `db.npgsql.connection_id`, `db.npgsql.data_source`, `db.npgsql.prepared`, `db.npgsql.rows`; event `received-first-response` |
| MYSQLCONNECTOR | `MySqlConnector` `2.6.2` | **Yes** | `MySqlConnector` | `2.0.0` | `private static ActivitySource ActivitySource { get; } = new("MySqlConnector", GetVersion());` — `src/MySqlConnector/Utilities/ActivitySourceHelper.cs` at tag `2.6.2`. Floor by bisect: the file exists at tag `2.0.0` (`CreateActivitySource()`, same name) and 404s at `1.3.14` and `2.0.0-beta.1`. Agrees with upstream `>=2.0.0`. | native | None for spans. Attribute flavor is environment-driven: without `OTEL_SEMCONV_STABILITY_OPT_IN=database` (or `database/dup`) only the experimental keys are set — `MySqlConnectorTracingOptions.GetDefaultSemanticConventions()` | `db.connection_id`, `db.connection_string`, `db.user`, `thread.id`, and, under the default experimental flavor, `db.name`, `db.system`, `db.statement`, `net.peer.ip`, `net.peer.name`, `net.peer.port`, `net.transport` |
| MYSQLDATA | `MySql.Data` `26.7.0` | **Yes** | `connector-net` | `8.1.0` | `Source = new("connector-net", version);` — `MySQL.Data/src/MySQLActivitySource.cs:47` at tag `9.7.0` (the newest public tag; `26.7.0` is untagged). The literal `connector-net` is present in the `#US` heap of `lib/net10.0/MySql.Data.dll` in the pinned `26.7.0` package. That `AddSource` alone suffices is Oracle's own statement: `MySql.Data.OpenTelemetry`'s entire content is `AddConnectorNet(this TracerProviderBuilder builder) => builder.AddSource("connector-net")`, `MySQL.Data.OpenTelemetry/src/TraceProviderBuilderExtension.cs`. Floor by bisect: the file exists at tag `8.1.0`, 404s at `8.0.33`. Agrees with upstream `>=8.1.0`. Guarded by `#if NET5_0_OR_GREATER`, so no .NET Framework. | native | None | `db.connection_string`, `db.name`, `db.sql.table`, `db.statement`, `db.system`, `db.user`, `net.peer.port`, `net.transport`, `otel.status_code`, `otel.status_description`, `thread.id`, `thread.name`; exception carried as an `exception` event, not `error.type` |
| ORACLEMDA | `Oracle.ManagedDataAccess.Core` `23.26.300` | **Yes** | `Oracle.ManagedDataAccess.Core` (ODP.NET Core; `Oracle.ManagedDataAccess` for the unmanaged driver) | `23.4.0` | Oracle documents the no-package path verbatim — "Without ODP.NET OpenTelemetry package … must call `AddSource("Oracle.ManagedDataAccess.Core")`; `AddOracleDataProviderInstrumentation()` is not required", <https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/featOpenTelemetry.html>. Floor by package bisect: `OpenTelemetryTracing` and the `Oracle.ManagedDataAccess.Core` source literal are in `lib/netstandard2.1/Oracle.ManagedDataAccess.dll` of `23.4.0` (first stable) and absent from `23.2.0-dev`. Agrees with upstream `>=23.4.0`. | native | None — `OracleConfiguration.OpenTelemetryTracing` is `public static bool` with default `true`, <https://docs.oracle.com/en/database/oracle/oracle-database/26/odpnt/ConfigurationOpenTelemetryTracing.html>. `DatabaseOpenTelemetryTracing` is a separate, additional span set and is not required | `db.odp.connection.id`, `db.odp.roundtrip.count`, `db.odp.roundtrip.duration`, `db.odp.rows_affected`, `db.odp.sql_id`, `db.odp.user.statement`, plus `db.name`, `db.statement`, `db.system`, `db.user`, `otel.status_code`, `otel.status_description` |
| STACKEXCHANGEREDIS | `StackExchange.Redis` `3.1.31` | No | n/a | n/a | `grep -rn ActivitySource` over `src/` at tag `3.1.31`: zero hits. The only instrumentation surface is the profiling API, `src/StackExchange.Redis/Profiling/ProfilingSession.cs`, which is what `OpenTelemetry.Instrumentation.StackExchangeRedis` drives — a contrib package by the rule's definition. | no native source | n/a | n/a |
| WCFCLIENT | `System.ServiceModel.Primitives` `10.0.652802` | No | n/a | n/a | String and metadata scan of every `System.ServiceModel*.dll` in the pinned package, `lib/` and `ref/`, `net10.0`, `net462` and `netstandard2.0`: zero `ActivitySource`, zero `DiagnosticListener`. `OpenTelemetry.Instrumentation.Wcf` works through an endpoint behavior, not a source. | no native source | n/a | n/a |

Four of the nine qualify: **NPGSQL**, **MYSQLCONNECTOR**, **MYSQLDATA**, **ORACLEMDA**. Five do not
and stay interceptors: **ADONET**, **SQLCLIENT**, **SQLITE**, **STACKEXCHANGEREDIS**, **WCFCLIENT**.

### Rows that carry a caveat

- **Npgsql** emits a second span, `CONNECT <database>`, when a physical connection is opened. The
  per-library demo proof is one span per command, not one span per run; the demo must open the
  connection outside the asserted window or assert the CONNECT span explicitly.
- **MySqlConnector** defaults to the *experimental* database conventions. Migrating without setting
  `OTEL_SEMCONV_STABILITY_OPT_IN=database` in the consumer's environment replaces qyl's stable
  `db.system.name` / `db.namespace` / `db.query.text` / `server.address` with `db.system` /
  `db.name` / `db.statement` / `net.peer.*`. Either the environment variable is part of the
  documented migration or the table-driven processor maps the legacy keys forward.
- **MySql.Data** is the sharpest output change in the wave. Its span is named `SQL Statement`, it
  carries no stable semantic-convention key at all, it reports failure as an `exception` event with
  `otel.status_code` rather than `error.type`, and it sets `db.statement` to
  `command.OriginalCommandText` unconditionally — qyl's `SET_DBSTATEMENT_FOR_TEXT` policy has no
  equivalent on the native source, so a processor would have to strip the tag rather than choose it.
  It also mutates the query itself, injecting a `traceparent` query attribute whenever
  `Activity.Current` is not null.
- **ODP.NET** produces several spans per command — `Connect`, `Open`, `Close`, `ExecuteNonQuery`,
  `SendExecuteRequest` — each display-named `<verb> HOST:PORT:DATABASE`, so span names carry the
  endpoint. Upstream also records it as unsupported on ARM64.
- **Microsoft.Data.SqlClient** is the one No that could plausibly change: the library has an open
  telemetry track and today ships `SqlClientMetrics` (a `Meter`) but no `ActivitySource`. Re-check
  at the next major rather than treating this row as permanent.

### What SemanticConventions 8.1.0 must carry for these four

Source-name constants: `Npgsql`, `MySqlConnector`, `connector-net`, `Oracle.ManagedDataAccess.Core`.

Vendor attribute namespaces: `db.npgsql.*` (4 keys) and `db.odp.*` (6 keys). The remaining keys the
four sources emit are not vendor-namespaced — they are pre-stable OpenTelemetry keys (`db.system`,
`db.name`, `db.statement`, `db.user`, `db.connection_id`, `db.connection_string`, `db.sql.table`,
`net.peer.*`, `net.transport`, `otel.status_*`, `thread.id`, `thread.name`) and the dropped-attribute
counter, not the registry, is the right place to make their loss visible.

## Every integration

The rule above is only checkable if it is applied to all of them. Mechanism today is read from
`lane` in `docs/generated/qyl-aot-contract.resolved.yaml`. Every row now carries a decided bucket;
where a row says "interceptor", the parenthesis names the reason, and the evidence for it is in the
audit tables above.

| Integration | Mechanism today | Bucket | Floor | After 13.0.0 |
| --- | --- | --- | --- | --- |
| MASSTRANSIT | interceptor | native | `8.0.0` | source + processor |
| ELASTICTRANSPORT | interceptor | native | `8.10.0` stack | source + processor |
| ELASTICSEARCH | interceptor | native, via transport | `8.10.0` stack | source + processor |
| QUARTZ | interceptor | native | `4.0.0` | source + processor |
| MONGODB | interceptor | native | `3.7.0` | source + processor |
| NSERVICEBUS | interceptor | native | `8.0` | source + processor |
| RABBITMQ | interceptor | native | `7.0.0` | source + processor |
| GRAPHQL | interceptor | native + consumer opt-in | `7.3.0` | interceptor (opt-in cannot be injected) |
| KAFKA | interceptor | no native source | n/a | interceptor |
| ADONET | interceptor | no native source | n/a | interceptor (`System.Data.Common` declares no `ActivitySource`) |
| SQLCLIENT | interceptor | `DiagnosticSource` only | n/a | interceptor (`SqlClientDiagnosticListener`, no `ActivitySource`) |
| SQLITE | interceptor | no native source | n/a | interceptor (`Microsoft.Data.Sqlite.Core` declares no `ActivitySource`) |
| NPGSQL | interceptor | native | `6.0.0` | source + processor |
| MYSQLCONNECTOR | interceptor | native | `2.0.0` | source + processor |
| MYSQLDATA | interceptor | native | `8.1.0` | source + processor |
| ORACLEMDA | interceptor | native | `23.4.0` | source + processor |
| STACKEXCHANGEREDIS | interceptor | no native source | n/a | interceptor (profiling API only) |
| WCFCLIENT | interceptor | no native source | n/a | interceptor (no `ActivitySource` in `System.ServiceModel.*`) |
| AZURE | source + framework initialization | native | — | unchanged; already the rule |
| WCFCORE | official library hook | native | — | unchanged; already the rule |
| MCP | official library hook | native | — | unchanged |
| MICROSOFTEXTENSIONSAI | official library hook | native + consumer opt-in | — | unchanged |
| MICROSOFTAGENTSAI | official library hook | native + consumer opt-in | — | unchanged |
| MICROSOFTAGENTSAIWORKFLOWS | official library hook | native + consumer opt-in | — | unchanged |
| ASPNETCORE, HTTPCLIENT, GRPCNETCLIENT, ENTITYFRAMEWORKCORE, ILOGGER, NETRUNTIME, PROCESS | runtime public telemetry | BCL/framework, out of scope | — | unchanged |
| ASPNET, WCFSERVICE | unsupported on NativeAOT | — | — | unchanged |
| LOG4NET, NLOG | not implemented | — | — | unchanged |

Every integration is audited. Eleven rows move to source + processor in the 13.0.0 wave —
MassTransit, Elastic.Transport, Elasticsearch (via the transport), Quartz, MongoDB, NServiceBus,
RabbitMQ, Npgsql, MySqlConnector, MySql.Data and ODP.NET — and seven stay interceptors with a
reason in the row: Kafka and GraphQL from the first pass, ADO.NET, SqlClient, Sqlite,
StackExchange.Redis and the WCF client from the second.

## Constraints on the 13.0.0 wave

1. **One processor**, extending the existing `Qyl.Telemetry.Hosting` processor rather than copied per
   library, driven by a single source-name table.
2. **Per library, a call-site-dependence check, with the result recorded.** The qyl attributes are
   constants per integration — `qyl.instrumentation.domain` and the instrumentation id — so a
   processor can normally set them without knowing the call site. What a call site supplies is
   semantic-convention data, which the native span already carries. This was verified end to end
   only for MassTransit, whose sole call-site-derived value is `messaging.operation.name`
   (`Publish` vs `Send`), supplied by MassTransit's own span. Every other library needs the same
   check before it moves; do not inherit this conclusion.
3. **Per library, demo-lane proof**: exactly one span from the native source, carrying the qyl
   attributes and the library's own attributes. One span, not two — a duplicate means the
   interceptor was not fully removed.
4. **The CHANGELOG names every output change** as old source and span name to new. This is what a
   consumer's dashboards and alerts are keyed on.
5. **The processor must stamp `qyl.instrumentation.domain` and the instrumentation id on every
   native span.** The qyl collector's span-attribute allowlist is the pinned semantic-convention
   registry plus `qyl.*`; vendor keys from the native sources (`quartz.*`, `elastic.transport.*`,
   `messaging.masstransit.*`, `nservicebus.*`) are dropped silently at ingest. Those two qyl-owned
   attributes are the only qyl facts that survive, and the dashboard classifies on attribute
   presence alone — those two plus semconv presence such as `messaging.system` and
   `db.system.name`. There is no span-name logic anywhere in the classification, so a span that
   reaches the collector without them is unclassifiable no matter what it is called.
6. **One commit per library**, each containing the interceptor deletion, the processor table row,
   the contract change and the demo-lane proof together. `main` is consistent and releasable after
   every commit, and the demo lane is green before the next one starts. MassTransit first. No
   `v13.0.0` tag until every row in the status table is closed as either migrated or
   stays-interceptor-with-reason.
