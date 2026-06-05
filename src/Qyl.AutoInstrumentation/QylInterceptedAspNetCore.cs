using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedAspNetCore
{

    public static WebApplication Build(WebApplicationBuilder builder)
    {
        if (builder is null)
            throw new NullReferenceException();

        var app = builder.Build();
        app.Use(static (context, next) => InvokeAsync(next, context));
        return app;
    }

    public static IEndpointConventionBuilder MapGet(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapGet(pattern, Observe(requestDelegate));

    public static IEndpointConventionBuilder MapPost(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPost(pattern, Observe(requestDelegate));

    public static IEndpointConventionBuilder MapPut(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPut(pattern, Observe(requestDelegate));

    public static IEndpointConventionBuilder MapDelete(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapDelete(pattern, Observe(requestDelegate));

    public static IEndpointConventionBuilder MapPatch(IEndpointRouteBuilder endpoints, string pattern, RequestDelegate requestDelegate)
        => endpoints.MapPatch(pattern, Observe(requestDelegate));

    public static IEndpointConventionBuilder MapMethods(IEndpointRouteBuilder endpoints, string pattern, IEnumerable<string> httpMethods, RequestDelegate requestDelegate)
        => endpoints.MapMethods(pattern, httpMethods, Observe(requestDelegate));

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
        var activity = QylActivitySource.Source.StartActivity(QylActivityNames.HttpServerRequest, ActivityKind.Server);
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
                options.AspNetCoreUrlQueryRedactionDisabled ? TrimQueryPrefix(query) : "REDACTED");
        }

        if (route is not null)
            activity.SetTag(QylSemanticAttributes.HttpRoute, route);

        SetConfiguredHeaders(activity, options.AspNetCoreCapturedRequestHeaderMap, context.Request.Headers);
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
        SetConfiguredHeaders(activity, QylAutoInstrumentationOptions.Current.AspNetCoreCapturedResponseHeaderMap, context.Response.Headers);
        if (context.Response.StatusCode >= 500)
        {
            activity.SetTag(QylSemanticAttributes.ErrorType, context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
            activity.SetStatus(ActivityStatusCode.Error);
        }
    }

    private static RequestDelegate Observe(RequestDelegate requestDelegate)
        => requestDelegate is null ? null! : context => InvokeAsync(requestDelegate, context);

    private static void SetConfiguredHeaders(Activity activity, QylCapturedNameMap configuredHeaders, IHeaderDictionary headers)
    {
        if (configuredHeaders.Count is 0)
            return;

        for (var index = 0; index < configuredHeaders.Count; index++)
        {
            if (headers.TryGetValue(configuredHeaders.GetLookupName(index), out var values) && values.Count > 0)
                activity.SetTag(configuredHeaders.GetTagName(index), values.Count is 1 ? values[0] : values.ToArray());
        }
    }

    private static string TrimQueryPrefix(string? query)
        => string.IsNullOrEmpty(query) || query[0] is not '?'
            ? query ?? string.Empty
            : query[1..];

    private static string? GetRoute(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
}
