using System;
using System.Diagnostics;
using System.IO;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>
/// Enriches ASP.NET Core's own server activity through <see cref="IStartupFilter"/>, so the qyl
/// attributes reach it without intercepting <c>WebApplicationBuilder.Build()</c>. Keeping the
/// injection off the call site means it never collides with a cooperating <c>Build()</c>
/// interceptor (CS9153).
/// </summary>
/// <remarks>
/// <para>
/// The span is <c>Microsoft.AspNetCore.Hosting.HttpRequestIn</c>, started by the hosting layer and
/// current for the whole pipeline. qyl starts no server activity of its own: a second one would be
/// a duplicate SERVER span for every request.
/// </para>
/// <para>
/// The runtime creates that activity <em>empty</em> — measured on .NET 10.0.11, it carries no tag at
/// all and keeps its raw operation name — so everything a consumer's dashboards key on is written
/// here, from the <see cref="HttpContext"/> the middleware already holds. Each write fills only what
/// is absent; a tag another component set is left alone.
/// </para>
/// </remarks>
internal sealed class QylAspNetCoreStartupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            // IStartupFilters compose in registration order and this one must stay outermost: the
            // request attributes are written before anything can short-circuit the pipeline, and the
            // response status is read after everything else has run.
            app.Use(static (context, requestDelegate) =>
                InvokeAsync(requestDelegate, context));
            next(app);
        };

    private static Task InvokeAsync(RequestDelegate requestDelegate, HttpContext context)
    {
        var activity = StartRequest(context);
        if (activity is null)
            return requestDelegate(context);

        try
        {
            return ObserveAsync(requestDelegate(context), context, activity);
        }
        catch (Exception exception)
        {
            RecordFailure(activity, context, exception);
            throw;
        }
    }

    private static Activity? StartRequest(HttpContext context)
    {
        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.AspNetCore))
            return null;

        // Only the hosting layer's own activity is the server span. Anything else current here is the
        // application's, and qyl does not write HTTP server attributes onto a span it does not know.
        if (Activity.Current is not { Kind: ActivityKind.Server, IsAllDataRequested: true } activity ||
            !StringComparer.Ordinal.Equals(activity.Source.Name, QylFrameworkActivitySources.AspNetCore))
        {
            return null;
        }

        var method = QylHttpMethod.Normalize(context.Request.Method, out var methodOriginal);
        QylHttpActivityPolicy.SetServerRequest(
            activity,
            method,
            methodOriginal,
            context.Request.Path.Value,
            context.Request.QueryString.HasValue ? context.Request.QueryString.Value![1..] : null,
            context.Request.Scheme);
        QylCaptureHelpers.SetRequestHeaders(activity, options.AspNetCoreCapturedRequestHeaderMap, context.Request.Headers);
        return activity;
    }

    private static async Task ObserveAsync(Task originalTask, HttpContext context, Activity activity)
    {
        try
        {
            await originalTask.ConfigureAwait(false);
        }
        catch (Exception exception)
        {
            RecordFailure(activity, context, exception);
            throw;
        }

        RecordResponse(activity, context, context.Response.StatusCode);
    }

    private static void RecordResponse(Activity activity, HttpContext context, int statusCode)
    {
        // Routing runs inside the pipeline, so the endpoint — and with it the route template and the
        // low-cardinality span name — is only knowable on the way out.
        QylHttpActivityPolicy.SetServerRoute(activity, QylHttpMethod.Normalize(context.Request.Method), GetRoute(context));
        QylHttpActivityPolicy.SetServerResponseStatus(activity, statusCode);
        QylCaptureHelpers.SetRequestHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.AspNetCoreCapturedResponseHeaderMap,
            context.Response.Headers);
    }

    // An exception that unwinds past this middleware has not reached the server yet, so the response
    // still carries whatever status the pipeline left on it. 500 is what Kestrel sends for it, and
    // what the span reports, unless the response had already started with a status of its own.
    //
    // The exception is Kestrel's own: a cancellation or I/O failure while the connection is already
    // aborted is a client that went away, not an application error. Kestrel sends nothing, logs the
    // request as 499 and raises no unhandled-exception event, so the span follows it — 499, no
    // error.type, status left unset. Measured on .NET 10: at the time this middleware sees the
    // exception the response still reads 200, and Kestrel writes its 499 only after the pipeline
    // has unwound, so the status is decided here from the same rule rather than read back later.
    private static void RecordFailure(Activity activity, HttpContext context, Exception exception)
    {
        if (context.RequestAborted.IsCancellationRequested && exception is OperationCanceledException or IOException)
        {
            RecordResponse(
                activity,
                context,
                context.Response.HasStarted ? context.Response.StatusCode : StatusCodes.Status499ClientClosedRequest);
            return;
        }

        // The exception type is the better error.type, so it is written first and the status-code
        // rule below finds the tag already set rather than replacing it with the bare "500".
        QylActivityStatus.RecordException(activity, exception);
        RecordResponse(activity, context, context.Response.HasStarted ? context.Response.StatusCode : 500);
    }

    private static string? GetRoute(HttpContext context)
        => context.GetEndpoint() is RouteEndpoint routeEndpoint
            ? routeEndpoint.RoutePattern.RawText
            : null;
}
