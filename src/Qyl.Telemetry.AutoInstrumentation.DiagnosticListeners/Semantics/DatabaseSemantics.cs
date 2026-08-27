namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class DatabaseSemantics
{
    public static bool ShouldWriteQueryText(string? queryText, bool captureText)
        => captureText && !string.IsNullOrWhiteSpace(queryText);
}
