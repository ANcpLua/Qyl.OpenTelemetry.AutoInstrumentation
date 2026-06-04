using System.Diagnostics;
using System.Globalization;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class HttpSemantics
{
    private static readonly HashSet<string> KnownMethods = new(StringComparer.Ordinal)
    {
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodConnect,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodDelete,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodGet,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodHead,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodOptions,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPatch,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPost,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodPut,
        global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodTrace,
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
        return global::Qyl.AutoInstrumentation.QylSemanticAttributes.HttpRequestMethodOther;
    }

    public static void SetUrlTags(Activity? activity, string? url, string? serverAddress, int? serverPort)
    {
        SemanticTagWriter.Set(activity, SemanticAttributes.UrlFull, url);

        Uri? uri = null;
        if (!string.IsNullOrWhiteSpace(url))
            Uri.TryCreate(url, UriKind.Absolute, out uri);

        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, serverAddress ?? uri?.Host);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerPort, serverPort ?? GetPort(uri));
    }

    public static void SetStatus(Activity? activity, ActivityKind kind, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(activity, SemanticAttributes.HttpResponseStatusCode, statusCode);

        var resolvedErrorType = ResolveErrorType(kind, statusCode, errorType);
        if (resolvedErrorType is null)
            return;

        SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType, resolvedErrorType);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static int? GetPort(Uri? uri)
        => uri is null || uri.IsDefaultPort ? null : uri.Port;

    private static string? ResolveErrorType(ActivityKind kind, int? statusCode, string? errorType)
    {
        if (!string.IsNullOrWhiteSpace(errorType))
            return errorType;

        if (statusCode is null)
            return null;

        var isError = kind switch
        {
            ActivityKind.Client => statusCode.Value >= 400,
            ActivityKind.Server => statusCode.Value >= 500,
            _ => statusCode.Value >= 500,
        };

        return isError
            ? statusCode.Value.ToString(CultureInfo.InvariantCulture)
            : null;
    }
}
