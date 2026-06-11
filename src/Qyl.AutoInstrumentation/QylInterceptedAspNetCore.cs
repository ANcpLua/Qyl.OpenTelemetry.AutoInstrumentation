using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted ASP.NET Core.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedAspNetCore);</code></example>
public static class QylInterceptedAspNetCore
{

    /// <summary>Runs the Build runtime helper used by source-generated qyl interceptors.</summary>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        if (builder is null)
            throw new NullReferenceException();

        var app = builder.Build();
        app.Use(static (context, next) => InvokeAsync(next, context));
        return app;
    }

    /// <summary>Runs the Map Get runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapGet(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapGet(pattern, Observe(requestDelegate));

    /// <summary>Runs the Map Post runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapPost(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPost(pattern, Observe(requestDelegate));

    /// <summary>Runs the Map Put runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapPut(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPut(pattern, Observe(requestDelegate));

    /// <summary>Runs the Map Delete runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapDelete(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapDelete(pattern, Observe(requestDelegate));

    /// <summary>Runs the Map Patch runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapPatch(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPatch(pattern, Observe(requestDelegate));

    /// <summary>Runs the Map Methods runtime helper used by source-generated qyl interceptors.</summary>
    public static IEndpointConventionBuilder MapMethods(IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> httpMethods, RequestDelegate requestDelegate)
        => endpoints.MapMethods(pattern, httpMethods, Observe(requestDelegate));

    /// <summary>Runs the Invoke Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task InvokeAsync(RequestDelegate requestDelegate, HttpContext context)
    {
        if (requestDelegate is null)
            throw new NullReferenceException();

        if (context is null)
            return requestDelegate(context!);

        var activity = StartRequestActivity(context);
        try
        {
            return ObserveAsync(requestDelegate(context), context, activity);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    private static Activity? StartRequestActivity(HttpContext context)
    {
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.AspNetCore))
            return null;

        var method = QylHttpMethod.Normalize(context.Request.Method);
        var route = GetRoute(context);
        var activity = QylActivitySource.StartActivity(QylActivityNames.HttpServerRequest, ActivityKind.Server);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.AspNetCoreServer);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);

        if (options.CaptureSensitiveValues)
            activity.SetTag(QylSemanticAttributes.UrlPath, context.Request.Path.Value);

        if (context.Request.QueryString.HasValue)
        {
            var query = context.Request.QueryString.Value;
            activity.SetTag(
                QylSemanticAttributes.UrlQuery,
                options.CaptureSensitiveValues && options.AspNetCoreUrlQueryRedactionDisabled
                    ? QylCaptureHelpers.TrimQueryPrefix(query)
                    : "REDACTED");
        }

        if (route is not null)
            activity.SetTag(QylSemanticAttributes.HttpRoute, route);

        QylCaptureHelpers.SetRequestHeaders(activity, options.AspNetCoreCapturedRequestHeaderMap, context.Request.Headers);
        return activity;
    }

    private static async Task ObserveAsync(Task originalTask, HttpContext context, Activity? activity)
    {
        if (activity is null)
        {
            await originalTask.ConfigureAwait(false);
            return;
        }

        try
        {
            await originalTask.ConfigureAwait(false);
            RecordResponse(activity, context);
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static void RecordResponse(Activity? activity, HttpContext context)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, context.Response.StatusCode);
        QylCaptureHelpers.SetRequestHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.AspNetCoreCapturedResponseHeaderMap,
            context.Response.Headers);
        if (context.Response.StatusCode >= 500)
        {
            activity.SetTag(QylSemanticAttributes.ErrorType, context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    private static RequestDelegate Observe(RequestDelegate requestDelegate)
        => requestDelegate is null ? null! : context => InvokeAsync(requestDelegate, context);

    private static string? GetRoute(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
}
