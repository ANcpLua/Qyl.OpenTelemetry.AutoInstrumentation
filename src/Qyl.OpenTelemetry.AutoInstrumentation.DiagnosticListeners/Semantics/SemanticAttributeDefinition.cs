namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal readonly record struct SemanticAttributeDefinition(
    string Key,
    SemanticStability Stability,
    string? ReplacedBy = null);
