using System.Diagnostics;
using System.Globalization;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class ErrorStatusSemantics
{
    public static string? ResolveHttpErrorType(ActivityKind kind, int? statusCode, string? errorType)
    {
        if (!string.IsNullOrWhiteSpace(errorType))
            return errorType;

        if (statusCode is null)
            return null;

        var minimumErrorStatusCode = kind switch
        {
            ActivityKind.Client => 400,
            ActivityKind.Server => 500,
            _ => 500,
        };

        return statusCode.Value >= minimumErrorStatusCode
            ? statusCode.Value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    public static string? ResolveGrpcErrorType(int? statusCode, string? errorType)
    {
        if (!string.IsNullOrWhiteSpace(errorType))
            return errorType;

        return statusCode is > 0
            ? statusCode.Value.ToString(CultureInfo.InvariantCulture)
            : null;
    }

    public static void SetError(Activity? activity, string? errorType)
    {
        if (string.IsNullOrWhiteSpace(errorType))
            return;

        SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType, errorType);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
