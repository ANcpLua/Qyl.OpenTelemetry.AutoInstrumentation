using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>
/// Registration surface for qyl ASP.NET Core server-request instrumentation.
/// </summary>
/// <remarks>
/// Adds a middleware-based server span (via <see cref="IStartupFilter"/>) that captures request and
/// response headers plus the query string. Prefer this when you want the richer middleware attributes.
/// Combining it with the zero-config <c>Qyl.OpenTelemetry.AutoInstrumentation.Hosting</c> module-init
/// path is safe: the single-owner signal registry lets the higher-priority middleware lane claim the
/// ASP.NET Core signal and the <c>DiagnosticListener</c> lane defer, so exactly one server span is
/// emitted per request either way.
/// </remarks>
public static class QylAspNetCoreInstrumentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the qyl ASP.NET Core server-span middleware. Idempotent — repeated calls keep a single
    /// registration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddQylAspNetCoreInstrumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // Claim the ASP.NET Core signal for the middleware lane so the DiagnosticListener lane (if the
        // Hosting package is also referenced) defers and the server span is emitted exactly once.
        QylSignalOwnership.Register(QylAutoInstrumentationIds.AspNetCore, QylSignalOwnership.GeneratedMiddleware);
        // IStartupFilters compose in registration order and this one must stay outermost so the server
        // span wraps the whole pipeline — call this before registering other pipeline-wrapping filters.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, QylAspNetCoreStartupFilter>());
        return services;
    }
}
