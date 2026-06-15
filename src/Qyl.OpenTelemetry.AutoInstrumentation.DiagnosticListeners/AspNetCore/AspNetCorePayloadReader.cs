using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.AspNetCore;

internal static class AspNetCorePayloadReader
{
    public static string? GetMethod(object? payload)
        => GetHttpContext(payload)?.Request.Method;

    public static string? GetRoute(object? payload)
    {
        var endpoint = GetHttpContext(payload)?.GetEndpoint();
        return endpoint is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
    }

    public static string? GetPath(object? payload)
        => GetHttpContext(payload)?.Request.Path.Value;

    public static int? GetStatusCode(object? payload)
        => GetHttpContext(payload)?.Response.StatusCode;

    private static HttpContext? GetHttpContext(object? payload)
        => payload as HttpContext;
}
