// qyl M9 fixture — second differentiator: an MCP client span.
// Emits an mcp.* CLIENT span via a qyl ActivitySource, captured via
// OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES="Qyl.M9.Mcp".
//
// MCP semantic conventions are very new — the mcp.* keys likely fall OUTSIDE the 1.41-based
// registry, so the conformance check should flag them verdict=unknown. rpc.system is an
// established (incubating) key, so it should be verdict=ok. This demonstrates conformance value
// on a frontier domain (mixed ok/unknown on real data).
// Observable output is deterministic (M9_DONE) for Gate B.

using System.Diagnostics;

using var source = new ActivitySource("Qyl.M9.Mcp");

using (var act = source.StartActivity("tools/call", ActivityKind.Client))
{
    act?.SetTag("mcp.method.name", "tools/call");   // expect verdict=unknown (frontier semconv)
    act?.SetTag("mcp.tool.name", "get_weather");     // expect verdict=unknown
    act?.SetTag("mcp.session.id", "sess-123");       // expect verdict=unknown
    act?.SetTag("mcp.transport", "stdio");           // expect verdict=unknown
    act?.SetTag("rpc.system", "jsonrpc");            // established key -> expect verdict=ok
}

Console.WriteLine("M9_DONE");
