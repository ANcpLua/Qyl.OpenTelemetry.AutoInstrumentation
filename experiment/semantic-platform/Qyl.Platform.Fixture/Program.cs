using Qyl.Generated.Logging;
using Qyl.Generated.OTel;
using Qyl.Platform.Fixture.Domain;

// OTelSemanticTags and LogScopeFields are produced by two INDEPENDENT consumer generators, neither
// of which references the producer. Both bound to the producer's pre-compilation [QylSemanticBinding]
// contract via the shared compilation. A green build that calls both is proof of 1-producer→N-consumer
// composition over a shared semantic graph.

var order = new CreateOrderRequest { CustomerId = "c-1", OrderId = "o-9", TenantId = "t-7", Amount = 42m };

Console.WriteLine("OTel consumer  (keyed by semantic-convention attribute):");
foreach (var kv in OTelSemanticTags.For(order))
    Console.WriteLine($"  activity.SetTag(\"{kv.Key}\", {kv.Value})");

Console.WriteLine("Logging consumer (keyed by property name — divergent projection of the SAME contract):");
foreach (var kv in LogScopeFields.For(order))
    Console.WriteLine($"  scope[\"{kv.Key}\"] = {kv.Value}");

Console.WriteLine();
Console.WriteLine("PLATFORM: 1 producer (pre-compilation contract) -> 2 consumers, neither referencing the producer.");
