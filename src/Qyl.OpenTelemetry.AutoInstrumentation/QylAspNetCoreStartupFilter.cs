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
            app.Use(static (context, requestDelegate) => QylInterceptedAspNetCore.InvokeAsync(requestDelegate, context));
            next(app);
        };
}
