using System.Diagnostics;
using System.Globalization;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class RpcSemantics
{
    public static void SetGrpcStatus(Activity? activity, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(activity, SemanticAttributes.RpcGrpcStatusCode, statusCode);

        var resolvedErrorType = errorType;
        if (string.IsNullOrWhiteSpace(resolvedErrorType) && statusCode is > 0)
            resolvedErrorType = statusCode.Value.ToString(CultureInfo.InvariantCulture);

        if (string.IsNullOrWhiteSpace(resolvedErrorType))
            return;

        SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType, resolvedErrorType);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
