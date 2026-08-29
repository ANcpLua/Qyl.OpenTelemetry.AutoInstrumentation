namespace Qyl.Telemetry.AutoInstrumentation;

using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;

internal static class QylHttpMethod
{
    public static bool IsKnown(string method)
        => method is not QylSemanticAttributes.HttpRequestMethodOther && HttpAttributes.RequestMethodValues.Contains(method);

    public static string Normalize(string? method)
    {
        if (string.IsNullOrEmpty(method))
            return QylSemanticAttributes.HttpRequestMethodOther;

        var normalized = method.ToUpperInvariant();
        return IsKnown(normalized) ? normalized : QylSemanticAttributes.HttpRequestMethodOther;
    }

    /// <summary>
    /// Normalizes <paramref name="method"/> and reports the raw value as <paramref name="original"/> when it
    /// is non-standard. Per OTel, http.request.method_original MUST be set whenever http.request.method is
    /// <c>_OTHER</c>; <paramref name="original"/> is null for the standard methods.
    /// </summary>
    public static string Normalize(string? method, out string? original)
    {
        var normalized = Normalize(method);
        original = !string.IsNullOrEmpty(method)
                   && string.Equals(normalized, QylSemanticAttributes.HttpRequestMethodOther, StringComparison.Ordinal)
            ? method
            : null;
        return normalized;
    }
}
