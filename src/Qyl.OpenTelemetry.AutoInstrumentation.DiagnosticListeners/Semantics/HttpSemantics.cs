using System.Diagnostics;
namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class HttpSemantics
{
    private static readonly HashSet<string> KnownMethods = new(StringComparer.Ordinal)
    {
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodConnect,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodDelete,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodGet,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodHead,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodOptions,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPatch,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPost,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPut,
        global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodTrace,
    };

    public static string? NormalizeMethod(string? method, out string? originalMethod)
    {
        originalMethod = null;

        if (string.IsNullOrWhiteSpace(method))
            return null;

        var normalized = method.Trim().ToUpperInvariant();
        if (KnownMethods.Contains(normalized))
        {
            if (!StringComparer.Ordinal.Equals(method, normalized))
                originalMethod = method;

            return normalized;
        }

        originalMethod = method;
        return global::Qyl.OpenTelemetry.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodOther;
    }

    public static void SetUrlTags(Activity? activity, string? url, string? serverAddress, int? serverPort)
    {
        SemanticTagWriter.Set(
            activity,
            SemanticAttributes.UrlFull,
            url is null
                ? null
                : global::Qyl.OpenTelemetry.AutoInstrumentation.Internal.QylCaptureHelpers.FormatUrlFull(
                    url,
                    global::Qyl.OpenTelemetry.AutoInstrumentation.QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled));

        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(url))
            Uri.TryCreate(url, UriKind.Absolute, out uri);

        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, serverAddress ?? uri?.Host);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerPort, serverPort ?? GetPort(uri));
    }

    public static void SetStatus(Activity? activity, ActivityKind kind, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpResponseStatusCode, statusCode);
        ErrorStatusSemantics.SetError(
            activity,
            ErrorStatusSemantics.ResolveHttpErrorType(kind, statusCode, errorType));
    }

    private static int? GetPort(Uri? uri)
        => uri is null || uri.IsDefaultPort ? null : uri.Port;
}
