using System.Diagnostics;
namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class RpcSemantics
{
    public static string? GetService(string? fullMethod)
    {
        var parts = SplitFullMethod(fullMethod);
        return parts.Service;
    }

    public static string? GetMethod(string? fullMethod)
    {
        var parts = SplitFullMethod(fullMethod);
        return parts.Method;
    }

    public static void SetGrpcStatus(Activity? activity, int? statusCode, string? errorType)
    {
        SemanticTagWriter.Set(activity, SemanticAttributes.RpcGrpcStatusCode, statusCode);
        ErrorStatusSemantics.SetError(activity, ErrorStatusSemantics.ResolveGrpcErrorType(statusCode, errorType));
    }

    private static (string? Service, string? Method) SplitFullMethod(string? fullMethod)
    {
        if (string.IsNullOrWhiteSpace(fullMethod))
            return default;

        var span = fullMethod.AsSpan().Trim();
        if (span.Length > 0 && span[0] == '/')
            span = span[1..];

        var separator = span.IndexOf('/');
        if (separator <= 0 || separator == span.Length - 1)
            return default;

        return (span[..separator].ToString(), span[(separator + 1)..].ToString());
    }
}
