---
name: qyl-telemetry
description: Instrument a .NET application for qyl through Qyl.Telemetry.Hosting. Use when adding spans, business attributes or failure recording to code that references Qyl.Telemetry.Hosting or calls builder.AddQyl().
---

# qyl telemetry in a .NET application

`AddQyl()` already records the HTTP server span, every HttpClient call, every Npgsql command, Kafka produce and receive and, after `UseOpenTelemetry()`, every chat and execute_tool span; add no `OpenTelemetry.Instrumentation.*` package and start no span for those operations.

```csharp
using Qyl;

builder.AddQyl(o => o.ServiceNamespace = "shop");
```

`service.name` defaults to `OTEL_SERVICE_NAME`, then the host application name; `service.version` comes from this build's version and commit, and `deployment.environment.name` from the host environment. Set `ServiceName`, `ServiceVersion` or `DeploymentEnvironment` only to override them. A request's W3C `baggage` member `session.id` becomes the server span's `session.id`, and every span below it inherits it.

## Services started by Qyl.Orchestration

Use [`Qyl.Orchestration`](https://github.com/ANcpLua/qyl/blob/main/packages/Qyl.Orchestration/README.md) in the host or test harness for process startup, service endpoints, readiness and shutdown. Each child application still references `Qyl.Telemetry.Hosting` and calls `AddQyl()`; the orchestrator supplies its process name as the default `OTEL_SERVICE_NAME`.

For an existing collector or test receiver accepting OTLP over HTTP/protobuf, set the child's export destination through its `QylProcessBuilder`:

```csharp
service.WithEnvironment("OTEL_EXPORTER_OTLP_ENDPOINT", receiverBaseUri.ToString())
    .WithEnvironment("OTEL_EXPORTER_OTLP_PROTOCOL", "http/protobuf");
```

Use the receiver's base URI, without `/v1/traces`. In an isolated test, `WithoutEnvironmentPrefix("OTEL_")` and `WithoutEnvironmentPrefix("QYL_")` remove inherited exporter settings before explicit values are applied. `WithReference(otherService)` supplies that service's HTTP address; `WaitFor(otherService)` controls startup order. The orchestration library does not start a collector or register telemetry in the child applications.

## Your own spans

Start them through `Qyl.Spans` and `Qyl.IncubatingSpans` (namespace `Qyl`), never with `new ActivitySource` or `StartActivity` (QYL0111, an error). Each starter names the span, sets its kind and `qyl.instrumentation.domain`, and returns `null` when nothing listens, so write `span?.`. The value sets (`DbSystemName`, `ErrorType`, `MessagingSystem`) are open structs in `Qyl.Telemetry.SemanticConventions.Activities` and `Qyl.Telemetry.SemanticConventions.Incubating.Activities`.

- `Spans.Work("checkout")`: business work no convention describes. The name is a constant (QYL0112).
- `Spans.DbClient(DbSystemName.Postgresql, collectionName: "orders", @namespace: "shop", operationName: "insert", serverPort: null)`: a store no subscribed library records, such as an in-memory one (`DbSystemName.Custom("memory")`). Every conditionally required parameter has no default, so `null` is written on purpose.
- `IncubatingSpans.MessagingProcessConsumer(MessagingSystem.Kafka, "process", batchMessageCount: null, destinationName: "orders", destinationTemplate: null, producer: producerContext)`: processing one received message.
- `IncubatingSpans.GenAiInvokeAgent(...)` and `GenAiExecuteTool(...)`: an agent or tool loop that Microsoft.Agents.AI does not run. Never inside an `AIFunctionFactory.Create` delegate (QYL0122): `FunctionInvokingChatClient` records that span already.

An `ActivityListener` of your own (a test, a sampler) selects the application's spans by name: `listener.ShouldListenTo = source => source.Name == QylTelemetryNames.Scopes.QylTelemetryApplication`. Never compare against `Spans.Source`: reading it inside `ShouldListenTo` runs `Spans`' type initializer, which consults the listeners while creating the source, and throws `TypeInitializationException`.

## Business attributes

`span?.SetAppTag("order.id", orderId)` writes `qyl.app.order.id` (string, long, double or bool). The key is a constant (QYL0112) and contains no token the collector denies (QYL0113): `token`, `secret`, `password`, `message`, `body`, `query`, `result`, `prompt` and the rest of `RegistryPolicy.DeniedKeyTokens`. At most 16 per span, each value up to 256 characters.

## Failures

```csharp
using var span = Spans.Work("charge card");
try
{
    await gateway.ChargeAsync(order);
}
catch (Exception exception)
{
    span?.RecordFailure(exception); // status Error with the message, error.type, the exception event
    throw;
}
```

A `catch` that rethrows inside a starter's scope without `RecordFailure` does not compile (QYL0110). A failure without an exception: `span?.RecordFailure(ErrorType.Custom("rate_limited"), "the provider answered 429")`.

## What the compiler cannot see

1. **The messaging parent context.** `MessagingProcessConsumer` takes the producer's `ActivityContext`, extracted from the message headers; `default` starts a new trace and cuts the message off from its sender.
2. **`UseOpenTelemetry()` on a custom source name.** `AddQyl()` subscribes the default Microsoft.Extensions.AI, Agents.AI and Workflows sources; `UseOpenTelemetry(sourceName: "...")` records spans no one exports. Keep the default name. (QYL1002 warns only when the call is missing.)
3. **Business keys.** Which facts identify the work (an order, a tenant, a plan) only the application knows: put them on its spans with `SetAppTag`.
