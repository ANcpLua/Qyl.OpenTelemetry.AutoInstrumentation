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

## Every integration

The rule above is only checkable if it is applied to all of them. Mechanism today is read from
`lane` in `docs/generated/qyl-aot-contract.resolved.yaml`. **"not audited" means exactly that** — no
one has yet checked whether the library owns a native `ActivitySource`, and the cell must not be
read as "no".

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
| ADONET | interceptor | **not audited** | — | unchanged pending audit |
| SQLCLIENT | interceptor | **not audited** | — | unchanged pending audit |
| SQLITE | interceptor | **not audited** | — | unchanged pending audit |
| NPGSQL | interceptor | **not audited** | — | unchanged pending audit |
| MYSQLCONNECTOR | interceptor | **not audited** | — | unchanged pending audit |
| MYSQLDATA | interceptor | **not audited** | — | unchanged pending audit |
| ORACLEMDA | interceptor | **not audited** | — | unchanged pending audit |
| STACKEXCHANGEREDIS | interceptor | **not audited** | — | unchanged pending audit |
| WCFCLIENT | interceptor | **not audited** | — | unchanged pending audit |
| AZURE | source + framework initialization | native | — | unchanged; already the rule |
| WCFCORE | official library hook | native | — | unchanged; already the rule |
| MCP | official library hook | native | — | unchanged |
| MICROSOFTEXTENSIONSAI | official library hook | native + consumer opt-in | — | unchanged |
| MICROSOFTAGENTSAI | official library hook | native + consumer opt-in | — | unchanged |
| MICROSOFTAGENTSAIWORKFLOWS | official library hook | native + consumer opt-in | — | unchanged |
| ASPNETCORE, HTTPCLIENT, GRPCNETCLIENT, ENTITYFRAMEWORKCORE, ILOGGER, NETRUNTIME, PROCESS | runtime public telemetry | BCL/framework, out of scope | — | unchanged |
| ASPNET, WCFSERVICE | unsupported on NativeAOT | — | — | unchanged |
| LOG4NET, NLOG | not implemented | — | — | unchanged |

Nine interceptors are unaudited, and several are plausible native-source candidates — Npgsql and
MySqlConnector in particular are worth checking first. Until they are audited this document states a
rule the codebase has not been proven to follow; that gap is the honest state, not an oversight to
paper over.

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
