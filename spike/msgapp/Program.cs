// qyl breadth fixture — MESSAGING domain (§07). Emits a semconv-shaped messaging.* PRODUCER span.
using System.Diagnostics;

using var source = new ActivitySource("Qyl.Breadth.Msg");
using (var act = source.StartActivity("send orders", ActivityKind.Producer))
{
    act?.SetTag("messaging.system", "kafka");
    act?.SetTag("messaging.operation.name", "send");
    act?.SetTag("messaging.destination.name", "orders");
    act?.SetTag("server.address", "broker.example.com");
}
Console.WriteLine("MSG_DONE");
