namespace Qyl.Platform.Fixture.Domain;

// User DTO. Declared partial so the producer's pre-compilation contract part (which carries the
// [QylSemanticBinding] attributes) merges into this same type. The user writes no attributes here.
public partial class CreateOrderRequest
{
    public string CustomerId { get; init; } = "";
    public string OrderId { get; init; } = "";
    public string TenantId { get; init; } = "";
    public decimal Amount { get; init; }
}
