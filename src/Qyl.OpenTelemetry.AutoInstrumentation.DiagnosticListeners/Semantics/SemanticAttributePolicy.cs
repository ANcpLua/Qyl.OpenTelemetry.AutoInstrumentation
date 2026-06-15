namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

using Qyl.OpenTelemetry.AutoInstrumentation;

internal sealed class SemanticAttributePolicy
{
    public static readonly SemanticAttributePolicy Current = new();

    private SemanticAttributePolicy()
    {
    }

    public bool ShouldWrite(SemanticAttributeDefinition attribute)
        => attribute.Stability is not SemanticStability.Deprecated;
}
