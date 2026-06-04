namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal readonly record struct SemanticAttributeDefinition(
    string Key,
    SemanticStability Stability,
    bool Sensitive = false,
    string? ReplacedBy = null);
