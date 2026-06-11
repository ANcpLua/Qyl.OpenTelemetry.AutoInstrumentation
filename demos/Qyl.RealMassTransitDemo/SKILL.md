---
name: qyl-real-masstransit-demo
description: Work on the real MassTransit qyl source-interceptor demo.
---

# MassTransit demo

Requires a real RabbitMQ broker endpoint for full proof. Spans come from source-generated
interceptors on `IPublishEndpoint.Publish` and `ISendEndpoint.Send`. Stay on the MassTransit
8.x line: 9.x is commercial. NativeAOT works because the demo chains a source-generated
`JsonSerializerContext` into MassTransit's serializer options; message types must live in a
namespace or MassTransit rejects them (the demo uses that rejection as its error path).
