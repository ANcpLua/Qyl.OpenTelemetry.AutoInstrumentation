namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

using Qyl.AutoInstrumentation;

internal sealed class SemanticAttributePolicy
{
    public static readonly SemanticAttributePolicy Current = new();

    private SemanticAttributePolicy()
    {
    }

    public bool ShouldWrite(SemanticAttributeDefinition attribute)
        => attribute.Stability is not SemanticStability.Deprecated;
}
