using System.Diagnostics;
using Qyl.Generated.Logging;
using Qyl.Generated.OTel;
using Qyl.Platform.Fixture.Domain;

// CreateOrderRequestTelemetry and LogScopeFields are produced by two INDEPENDENT consumer generators,
// neither of which references the producer; both bound the producer's pre-compilation [QylSemanticBinding]
// contract via the shared compilation. The OTel recorder is pure SetTag — AOT-safe, no reflection.

using var listener = new ActivityListener
{
    ShouldListenTo = s => s.Name == "Qyl.Demo",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
};
ActivitySource.AddActivityListener(listener);
var source = new ActivitySource("Qyl.Demo");

var order = new CreateOrderRequest { CustomerId = "c-1", OrderId = "o-9", TenantId = "t-7", Amount = 42m };

using (var activity = source.StartActivity("create-order"))
{
    // Generated telemetry — part of the application binary, not a runtime hook.
    CreateOrderRequestTelemetry.Record(activity, order);

    Console.WriteLine("Generated telemetry set these tags on a LIVE System.Diagnostics.Activity:");
    foreach (var tag in activity!.TagObjects)
        Console.WriteLine($"  {tag.Key} = {tag.Value}");
}

Console.WriteLine();
Console.WriteLine("Logging consumer (same contract, projected by property name):");
foreach (var kv in LogScopeFields.For(order))
    Console.WriteLine($"  scope[\"{kv.Key}\"] = {kv.Value}");

Console.WriteLine();
Console.WriteLine("PLATFORM: 1 producer (pre-compilation contract) -> N consumers; telemetry is compiled INTO the app.");
