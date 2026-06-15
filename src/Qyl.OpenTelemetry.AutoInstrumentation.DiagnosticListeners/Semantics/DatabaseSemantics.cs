using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class DatabaseSemantics
{
    private static readonly HashSet<string> KnownOperations = new(StringComparer.Ordinal)
    {
        "ALTER",
        "CALL",
        "CREATE",
        "DELETE",
        "DROP",
        "EXEC",
        "EXECUTE",
        "INSERT",
        "MERGE",
        "SELECT",
        "TRUNCATE",
        "UPDATE",
    };

    public static string? NormalizeOperation(string? operation, string? queryText)
    {
        var candidate = !string.IsNullOrWhiteSpace(operation)
            ? operation
            : FirstQueryToken(queryText);

        if (string.IsNullOrWhiteSpace(candidate))
            return null;

        var normalized = candidate.Trim().ToUpperInvariant();
        return KnownOperations.Contains(normalized) ? normalized : "_OTHER";
    }

    public static void SetError(Activity? activity, string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType))
            return;

        SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType, errorType);
        activity?.SetStatus(System.Diagnostics.ActivityStatusCode.Error);
    }

    public static string? CreateSummary(string? operation, string? source)
    {
        if (string.IsNullOrWhiteSpace(operation))
            return null;

        return string.IsNullOrWhiteSpace(source)
            ? operation
            : $"{source.Trim()} {operation}";
    }

    public static bool ShouldWriteQueryText(string? queryText, string? operation, bool captureText)
        => !string.IsNullOrWhiteSpace(queryText) &&
           captureText;

    private static string? FirstQueryToken(string? queryText)
    {
        if (string.IsNullOrWhiteSpace(queryText))
            return null;

        var span = queryText.AsSpan().TrimStart();
        var end = 0;
        while (end < span.Length && char.IsLetter(span[end]))
            end++;

        return end is 0 ? null : span[..end].ToString();
    }
}
