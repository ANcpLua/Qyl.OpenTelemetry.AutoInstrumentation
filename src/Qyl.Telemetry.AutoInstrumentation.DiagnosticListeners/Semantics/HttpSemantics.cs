using System.Diagnostics;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;
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
        return HttpAttributes.RequestMethodValues.Other;
    }

    public static void SetUrlTags(Activity? activity, string? url, string? serverAddress, int? serverPort)
    {
        SemanticTagWriter.Set(
            activity,
            UrlAttributes.Full,
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
            ServerAttributes.Address,
            serverAddress ?? uri?.Host);
        SemanticTagWriter.Set(
            activity,
            ServerAttributes.Port,
            serverPort ?? GetPort(uri));
    }

    public static void SetStatus(Activity? activity, ActivityKind kind, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(
            activity,
            HttpAttributes.ResponseStatusCode,
            statusCode);
        ErrorStatusSemantics.SetError(
            activity,
            ErrorStatusSemantics.ResolveHttpErrorType(kind, statusCode, errorType));
    }

    private static int? GetPort(Uri? uri)
        => uri is null || uri.IsDefaultPort ? null : uri.Port;
}
