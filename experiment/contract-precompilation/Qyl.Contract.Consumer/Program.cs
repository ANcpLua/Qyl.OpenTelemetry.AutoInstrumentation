using Qyl.Generated.Contract;

// Both namespaces below are produced ONLY by the two-phase generator:
//   ContractRegistry  -> pre-compilation phase (contract as symbols)
//   ContractBinding   -> standard phase (bound the pre-comp symbols + inferred user-DTO semconv)
// A green build that references them is proof the whole contract-as-symbols pipeline works.

Console.WriteLine($"[pre-comp]  ContractRegistry.CapabilityCount = {ContractRegistry.CapabilityCount}");
Console.WriteLine($"[standard]  ContractBinding.BoundCapabilityCount = {ContractBinding.BoundCapabilityCount}  (bound via GetTypeByMetadataName)");

// The 4 unsupported items must remain unsupported — prove the symbol carries that truth.
var aspnet = ContractRegistry.Capabilities.First(c => c.Key == "signals.traces.ASPNET");
Console.WriteLine($"[truth]     signals.traces.ASPNET status = {aspnet.Status}  (stays UnsupportedNativeAot)");
Console.WriteLine($"[truth]     ContractRegistry.IsImplemented(\"signals.traces.ASPNET\") = {ContractRegistry.IsImplemented("signals.traces.ASPNET")}");
Console.WriteLine($"[truth]     ContractRegistry.IsImplemented(\"signals.traces.ADONET\")  = {ContractRegistry.IsImplemented("signals.traces.ADONET")}");

Console.WriteLine();
Console.WriteLine($"[H2] compile-time-only semconv inference ({ContractBinding.InferredAttributeCount} attributes, impossible at runtime without reflection):");
foreach (var (type, prop, attr) in ContractBinding.InferredBindings)
    Console.WriteLine($"     {type}.{prop}  ->  {attr}");

// Touch the DTOs so they are emitted into the compilation the standard phase inspects.
_ = new Qyl.Contract.Consumer.Contracts.OrderRequest("c", "t", "o", 0m);
_ = new Qyl.Contract.Consumer.Contracts.ShipmentEvent("o", "x", "dhl");
