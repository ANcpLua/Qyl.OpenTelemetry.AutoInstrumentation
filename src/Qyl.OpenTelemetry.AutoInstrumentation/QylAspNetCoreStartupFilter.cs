using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Injects the qyl ASP.NET Core server-request middleware through <see cref="IStartupFilter"/>, so the
/// per-request server <see cref="System.Diagnostics.Activity"/> is wired without intercepting
/// <c>WebApplicationBuilder.Build()</c>. Keeping the injection off the call site means it never collides
/// with a cooperating <c>Build()</c> interceptor (CS9153), while preserving the exact middleware
/// semantics — request/response header capture and query-string recording — of the former interceptor path.
/// </summary>
internal sealed class QylAspNetCoreStartupFilter : IStartupFilter
{
    /// <inheritdoc/>
    public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) =>
        app =>
        {
            // Middleware lane (priority 90). Defers to the endpoint interceptor lane (95) when it owns the
            // ASP.NET Core signal, so a consumer that has both intercepted endpoints and this middleware
            // still emits exactly one server span. Wins over the DiagnosticListener lane (70).
            app.Use(static (context, requestDelegate) =>
                QylSignalOwnership.ShouldEmit(QylAutoInstrumentationIds.AspNetCore, QylSignalOwnership.GeneratedMiddleware)
                    ? QylInterceptedAspNetCore.InvokeAsync(requestDelegate, context)
                    : requestDelegate(context));
            next(app);
        };
}
