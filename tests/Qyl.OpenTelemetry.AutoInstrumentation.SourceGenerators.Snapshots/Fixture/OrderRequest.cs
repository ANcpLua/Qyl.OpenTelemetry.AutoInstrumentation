namespace Qyl.Snapshot.Domain;

// Partial so the producer's pre-compilation [QylSemanticBinding] contract part merges into this type.
// The user writes no attributes; they come from app.qyl-semantic-contract.tsv via the producer.
public partial class OrderRequest
{
    public string CustomerId { get; init; } = "";
    public string OrderId { get; init; } = "";
}
