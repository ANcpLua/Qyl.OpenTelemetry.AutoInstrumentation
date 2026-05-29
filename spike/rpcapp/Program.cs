// qyl breadth fixture — RPC domain (§07). Emits a semconv-shaped rpc.* CLIENT span.
using System.Diagnostics;

using var source = new ActivitySource("Qyl.Breadth.Rpc");
using (var act = source.StartActivity("oteldemo.CartService/GetCart", ActivityKind.Client))
{
    act?.SetTag("rpc.system", "grpc");
    act?.SetTag("rpc.service", "oteldemo.CartService");
    act?.SetTag("rpc.method", "GetCart");
    act?.SetTag("server.address", "cart.example.com");
}
Console.WriteLine("RPC_DONE");
