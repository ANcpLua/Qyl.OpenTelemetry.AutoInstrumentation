namespace Qyl.Contract.Consumer.Contracts;

// A user DTO. Its property shapes are visible to the compiler but NOT to runtime DiagnosticListeners
// (which would need reflection — forbidden). The standard phase infers semconv bindings from these.
public sealed record OrderRequest(string CustomerId, string TenantId, string OrderId, decimal Amount);

public sealed record ShipmentEvent(string OrderId, string CorrelationId, string Carrier);
