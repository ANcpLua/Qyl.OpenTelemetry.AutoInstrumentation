// qyl M5 fixture — emits a custom span carrying a DELIBERATELY non-semconv attribute key, to
// prove the conformance check's verdict=unknown path end-to-end on REAL telemetry (M4 only ever
// saw verdict=ok on real data; the unknown path was proven synthetically by the self-test).
//
// The span source is captured by the substrate via:
//   OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES="Qyl.M5.Demo"
// Observable output is deterministic (M5_DONE) and independent of attach — for Gate B.

using System.Diagnostics;

using var source = new ActivitySource("Qyl.M5.Demo");

using (var activity = source.StartActivity("m5-unknown-key-demo"))
{
    activity?.SetTag("qyl.custom.unmapped", "demo"); // NOT in the registry -> verdict=unknown
    activity?.SetTag("http.request.method", "GET");  // real semconv key    -> verdict=ok
}

Console.WriteLine("M5_DONE");
