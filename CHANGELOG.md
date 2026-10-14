# Changelog

Notable changes to the `Qyl.Telemetry.*` package family. Versions are owned by `<Version>` in
`Directory.Build.props`. The local release gates run before `./release.sh` packs and publishes
that exact version, creates the matching `v*` tag and GitHub release, and waits for all package indexes. Indexed
packages are then exercised in clean managed and NativeAOT consumers.

## [25.0.1] - 2026-10-15

### Changed

- Qyl.Telemetry.SemanticConventions 10.0.1. `Qyl.Telemetry.Hosting` carries its analyzers to consumers, so QYL0113 now reports `SetAppTag` keys whose name the registry does not persist (`SetAppTag("user.email", ...)` is `qyl.app.user.email`, dropped like `user.email`). The persistence table also treats plain token counts as token-exempt and four vendor keys as not persisted.

## [25.0.0] - 2026-10-14

The agent-first API of the qyl API DSL ADR (2026-10-14), on `Qyl.Telemetry.SemanticConventions`
10.0.0: a breaking release.

### Added

- `AddQyl()` subscribes the application scope `Qyl.Telemetry.Application` behind `Qyl.Spans.Source`,
  unconditionally: a span from a generated starter or `Spans.Work` is exported with no
  `ActivitySource` and no `AddSource` in application code.

- `AddQyl()` derives the resource (ADR D5): `service.version` defaults to the consuming build's own
  version and source revision (`InformationalVersion`, e.g. `1.4.0+3f2a9c1`), which the package's
  build assets expose to the generator as compiler-visible properties and the generator compiles
  into the application as `QylConsumerBuild` (constants `Version` and `Revision`; its module
  initializer hands the version over, no reflection); `deployment.environment.name` defaults to
  `IHostEnvironment.EnvironmentName`. New options `ServiceNamespace` (`service.namespace`) and
  `DeploymentEnvironment`.
- The session spine: the ASP.NET Core middleware stamps the request's W3C `baggage` member
  `session.id` onto the server span (a session id only: unreserved URI characters, at most 128,
  not dots alone), which `QylSessionSpanProcessor` carries to every in-process descendant. Every
  ASP.NET Core service joins an agent's session without code; `Qyl.Api`'s own filter is redundant.
- `QYL1002` reports, at Warning, `Microsoft.Extensions.AI`, `Microsoft.Agents.AI` and
  `Microsoft.Agents.AI.Workflows` used without `UseOpenTelemetry()` / `WithOpenTelemetry()`: their
  `ActivitySource`s stay silent without it. GraphQL.NET and ODP.NET stay at Info.

- `Qyl.Telemetry.Hosting` carries `SKILL.md` at its package root (ADR D7): the guide for a coding
  agent in the consuming application. It opens with what `AddQyl()` already records, then the
  starters, `Work`, `SetAppTag` and `RecordFailure`, and the three things the compiler cannot see
  (the messaging parent context, `UseOpenTelemetry()` on a custom source name, business keys).
  `./eng/build.sh PackageLayout` checks it is packed.

### Removed

- `QylTelemetryOptions.ResourceAttributes`, the last string-keyed slot: the resource is derived
  (`ServiceName`, `ServiceNamespace`, `ServiceVersion`, `DeploymentEnvironment`), and
  `OTEL_RESOURCE_ATTRIBUTES` still reaches the SDK.

### Changed

- The semantic-convention analyzers flow to every consumer: the core package references
  `Qyl.Telemetry.SemanticConventions.Analyzers` with `PrivateAssets="none"`, so its nuspec
  dependency excludes neither analyzers nor build assets, and a `Qyl.Telemetry.Hosting` consumer gets
  every `QYL0xxx` rule, among them QYL0111 (no hand-made `ActivitySource` or raw `StartActivity` in
  application code) at Error. `./eng/build.sh PackageLayout` checks the dependency;
  `ConsumerBehavior` builds a package consumer whose `new ActivitySource` fails with QYL0111.
- **A failure is recorded with `Spans.RecordFailure`** (ADR D4), the call application code makes: the
  generated interceptors, the asynchronous observer, the ADO.NET command interceptor and the ASP.NET
  Core middleware now set status Error *with the exception's message as the description* (bounded
  to 256 characters; the one message carrier that survives ingest), `error.type`, and the exception
  event. `RecordException` gave neither the description nor the event. `error.type` of a generic
  exception stays bounded (`Confluent.Kafka.ProduceException`2`).
- The generated-code helper `QylInterceptedActivity.RecordException` is `RecordFailure`.
- Span names come from the registry's `qyl.span_name` chains (`Qyl.SpanNames`, generated in the
  semantic-convention package) with the attributes each span records, so interceptors and the
  generated starters name spans the same way; `QylSpanNames` is deleted. One name changes: a
  StackExchange.Redis command span is `{db.operation.name} {db.namespace}`, the database index
  (`GET 0`), where it was the command alone.
- The session-propagation lane's third property is the stamp: the second process's server span
  carries the session that reached it in baggage, with no `Qyl.Api`, and its child inherits it. The
  ASP.NET Core demo drops its own baggage filter and proves the middleware's.
- The Npgsql and HttpClient demos run their failing call inside the application's own `Spans.Work`
  span and assert the `RecordFailure` triple on it; the Kafka demo asserts it on the interceptor's
  failed producer span.
- The qyl `ActivitySource` carries the qyl registry's schema URL (`SchemaUrl.Qyl`,
  `https://qyl.at/schemas/10.0.0`), like the application scope.
- README: the QYL1002 rows it already claimed exist; the one source name still typed by hand
  (`Experimental.ModelContextProtocol`) is named; the domain list is the registry's whole value set,
  `db.elasticsearch` and the four application domains included.
- The ReSharper inspection batch of 2026-10-14 over `src/` (67 findings): redundant usings and
  qualifiers, declarations folded into their null checks, the redundant `AttributeUsage`
  properties, braces, the generator's unused receiver parameter and never-read
  `IntegrationDeclaration.Domain`, the never-read `SqlClientCommand.OperationId`; the ASP.NET Core
  middleware observes a synchronous throw and a faulted task in one async path. No behavior change.
- The ASP.NET Core, MCP and session-propagation demos start their own spans with `Spans.Work`
  instead of a hand-made source added through `AdditionalSources`.
- Every `Qyl.Telemetry.SemanticConventions*` package is pinned at 10.0.0 (`Directory.Packages.props`
  remains the one place that names it).
- The generated-code ABI anchor is `QylGeneratedCodeAbi.V25`; interceptors generated by a 24.x
  generator no longer compile against this runtime.

## [24.1.1] - 2026-10-14

### Security

- `Qyl.Telemetry.AutoInstrumentation.SqlClient` depends on `System.Security.Cryptography.Pkcs`
  10.0.13. `Microsoft.Data.SqlClient` 7.1.1, the newest release, brings 9.0.18, which the
  advisories of 2026-10-13 (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) cover; the floor reaches
  consumers through the package.

### Changed

- The build host and the SqlClient, Oracle and WCF demos reference the patched
  `System.Security.Cryptography.Pkcs` and `.Xml` 10.0.13 (Fallout.Common brought 10.0.10 of both;
  GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83); NuGet audit failed those
  restores.
- The demo lanes' local collector is the `qyl` tool 12.2.0.
