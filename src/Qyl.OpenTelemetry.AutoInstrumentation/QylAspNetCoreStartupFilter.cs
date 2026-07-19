using System;
using System.Diagnostics;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Injects the qyl ASP.NET Core server-request middleware through <see cref="IStartupFilter"/>, so the
/// per-request server <see cref="System.Diagnostics.Activity"/> is wired without intercepting
/// <c>WebApplicationBuilder.Build()</c>. Keeping the injection off the call site means it never collides
/// with a cooperating <c>Build()</c> interceptor (CS9153). The middleware owns request/response
/// header capture and query-string recording.
/// </summary>
internal sealed class QylAspNetCoreStartupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            // The middleware lane owns the ASP.NET Core signal when registered, so a DiagnosticListener
            // subscriber defers and each request emits one server span.
            app.Use(static (context, requestDelegate) =>
                InvokeAsync(requestDelegate, context));
            next(app);
        };

    private static Task InvokeAsync(RequestDelegate requestDelegate, HttpContext context)
    {
        var activity = StartRequestActivity(context);
        try
        {
            return ObserveAsync(requestDelegate(context), context, activity);
        }
        catch (Exception exception)
        {
            QylActivityStatus.RecordException(activity, exception);
            activity?.Dispose();
            throw;
        }
    }

    private static Activity? StartRequestActivity(HttpContext context)
    {
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.AspNetCore))
            return null;

        var method = QylHttpMethod.Normalize(context.Request.Method, out var methodOriginal);
        var activity = QylHttpActivityPolicy.StartServerActivity(
            method,
            methodOriginal,
            GetRoute(context),
            context.Request.Path.Value,
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : null,
            context.Request.Scheme);
        if (activity is null)
            return null;

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
            QylActivityStatus.RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    private static void RecordResponse(Activity activity, HttpContext context)
    {
        QylHttpActivityPolicy.BackfillServerRoute(activity, QylHttpMethod.Normalize(context.Request.Method), GetRoute(context));
        QylHttpActivityPolicy.SetResponseStatus(activity, context.Response.StatusCode, 500);
        QylCaptureHelpers.SetRequestHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.AspNetCoreCapturedResponseHeaderMap,
            context.Response.Headers);
    }

    private static string? GetRoute(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
}
