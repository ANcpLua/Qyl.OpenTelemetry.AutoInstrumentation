using System.Diagnostics;
namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class HttpSemantics
{
    public static string? NormalizeMethod(string? method, out string? originalMethod)
    {
        originalMethod = null;

        if (string.IsNullOrWhiteSpace(method))
            return null;

        var normalized = method.Trim().ToUpperInvariant();
        if (global::Qyl.Telemetry.AutoInstrumentation.QylHttpMethod.IsKnown(normalized))
        {
            if (!StringComparer.Ordinal.Equals(method, normalized))
                originalMethod = method;

            return normalized;
        }

        originalMethod = method;
        return global::Qyl.Telemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodOther;
    }

    public static void SetUrlTags(Activity? activity, string? url, string? serverAddress, int? serverPort)
    {
        SemanticTagWriter.Set(
            activity,
            global::Qyl.Telemetry.AutoInstrumentation.QylSemanticAttributes.UrlFull,
            url is null
                ? null
                : global::Qyl.Telemetry.AutoInstrumentation.Internal.QylCaptureHelpers.FormatUrlFull(
                    url,
                    global::Qyl.Telemetry.AutoInstrumentation.QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled));

        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(url))
            Uri.TryCreate(url, UriKind.Absolute, out uri);

        SemanticTagWriter.Set(
            activity,
            global::Qyl.Telemetry.AutoInstrumentation.QylSemanticAttributes.ServerAddress,
            serverAddress ?? uri?.Host);
        SemanticTagWriter.Set(
            activity,
            global::Qyl.Telemetry.AutoInstrumentation.QylSemanticAttributes.ServerPort,
            serverPort ?? GetPort(uri));
    }

    public static void SetStatus(Activity? activity, ActivityKind kind, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(
            activity,
            global::Qyl.Telemetry.AutoInstrumentation.QylSemanticAttributes.HttpResponseStatusCode,
            statusCode);
        ErrorStatusSemantics.SetError(
            activity,
            ErrorStatusSemantics.ResolveHttpErrorType(kind, statusCode, errorType));
    }

    private static int? GetPort(Uri? uri)
        => uri is null || uri.IsDefaultPort ? null : uri.Port;
}
