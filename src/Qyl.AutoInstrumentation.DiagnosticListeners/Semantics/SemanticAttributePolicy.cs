namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal sealed class SemanticAttributePolicy
{
    private const string CaptureSensitiveValuesName = "QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES";

    public static readonly SemanticAttributePolicy Current = new();

    private SemanticAttributePolicy()
    {
        CaptureSensitiveValues = ReadBoolean(CaptureSensitiveValuesName);
    }

    public bool CaptureSensitiveValues { get; }

    public bool ShouldWrite(SemanticAttributeDefinition attribute)
        => attribute.Stability is not SemanticStability.Deprecated &&
           (!attribute.Sensitive || CaptureSensitiveValues);

    private static bool ReadBoolean(string name)
        => Environment.GetEnvironmentVariable(name)?.Trim().ToLowerInvariant() switch
        {
            "1" or "true" or "yes" or "on" => true,
            _ => false,
        };
}
