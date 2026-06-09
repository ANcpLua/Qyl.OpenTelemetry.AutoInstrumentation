namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

using Qyl.AutoInstrumentation;

internal sealed class SemanticAttributePolicy
{
    public static readonly SemanticAttributePolicy Current = new();

    private SemanticAttributePolicy()
    {
        CaptureSensitiveValues = QylAutoInstrumentationOptions.Current.CaptureSensitiveValues;
    }

    public bool CaptureSensitiveValues { get; }

    public bool ShouldWrite(SemanticAttributeDefinition attribute)
        => attribute.Stability is not SemanticStability.Deprecated &&
           (!attribute.Sensitive || CaptureSensitiveValues);
}
