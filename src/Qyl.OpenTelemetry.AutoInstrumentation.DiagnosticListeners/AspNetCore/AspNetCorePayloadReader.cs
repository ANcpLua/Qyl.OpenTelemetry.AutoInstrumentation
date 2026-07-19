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

    public static string? GetQuery(object? payload)
    {
        var query = GetHttpContext(payload)?.Request.QueryString;
        return query is { HasValue: true } value ? value.Value![1..] : null;
    }

    public static IHeaderDictionary? GetRequestHeaders(object? payload)
        => GetHttpContext(payload)?.Request.Headers;

    public static IHeaderDictionary? GetResponseHeaders(object? payload)
        => GetHttpContext(payload)?.Response.Headers;

    private static HttpContext? GetHttpContext(object? payload)
        => payload as HttpContext;
}
