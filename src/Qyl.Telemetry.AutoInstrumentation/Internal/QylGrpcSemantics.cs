using System.Diagnostics;
using System.Globalization;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylGrpcSemantics
{
    internal const string OtherMethod = "_OTHER";

    public static string? NormalizeMethod(string? method, out string? originalMethod)
    {
        originalMethod = null;

        if (string.IsNullOrWhiteSpace(method))
            return null;

        var normalized = method.Trim().Trim('/');
        if (normalized.Length is 0)
            return null;

        if (!StringComparer.Ordinal.Equals(normalized, "other"))
            return normalized;

        originalMethod = normalized;
        return OtherMethod;
    }

    public static void SetStatus(Activity? activity, int? statusCode)
    {
        if (activity is null || statusCode is null)
            return;

        var statusName = GetStatusName(statusCode.Value);
        activity.SetTag(RpcAttributes.ResponseStatusCode, statusName);
        if (statusCode.Value is 0)
            return;

        activity.SetTag(ErrorAttributes.Type, statusName);
        activity.SetStatus(ActivityStatusCode.Error);
    }

    private static string GetStatusName(int statusCode)
        => statusCode switch
        {
            0 => "OK",
            1 => "CANCELLED",
            2 => "UNKNOWN",
            3 => "INVALID_ARGUMENT",
            4 => "DEADLINE_EXCEEDED",
            5 => "NOT_FOUND",
            6 => "ALREADY_EXISTS",
            7 => "PERMISSION_DENIED",
            8 => "RESOURCE_EXHAUSTED",
            9 => "FAILED_PRECONDITION",
            10 => "ABORTED",
            11 => "OUT_OF_RANGE",
            12 => "UNIMPLEMENTED",
            13 => "INTERNAL",
            14 => "UNAVAILABLE",
            15 => "DATA_LOSS",
            16 => "UNAUTHENTICATED",
            _ => statusCode.ToString(CultureInfo.InvariantCulture),
        };
}
