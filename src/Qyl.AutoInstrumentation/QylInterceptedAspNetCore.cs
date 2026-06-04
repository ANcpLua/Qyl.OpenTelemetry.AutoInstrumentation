using System.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedAspNetCore
{
    private const string AspNetCoreDomain = "aspnetcore.server";

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
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.AspNetCore))
            return null;

        var method = context.Request.Method;
        var route = GetRoute(context);
        var activityName = route is null ? "HTTP " + method : method + " " + route;
        var activity = QylActivitySource.Source.StartActivity(activityName, ActivityKind.Server);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, AspNetCoreDomain);
        activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);
        activity.SetTag(QylSemanticAttributes.UrlPath, context.Request.Path.Value);

        if (route is not null)
            activity.SetTag(QylSemanticAttributes.HttpRoute, route);

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
            activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, context.Response.StatusCode);
            if (context.Response.StatusCode >= 500)
            {
                activity.SetTag(QylSemanticAttributes.ErrorType, context.Response.StatusCode.ToString(System.Globalization.CultureInfo.InvariantCulture));
                activity.SetStatus(ActivityStatusCode.Error);
            }
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

    private static string? GetRoute(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
}
