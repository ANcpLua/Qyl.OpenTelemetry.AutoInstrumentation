using System;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>
/// Registration surface for qyl ASP.NET Core server-request instrumentation.
/// </summary>
/// <remarks>
/// The server span is ASP.NET Core's own <c>Microsoft.AspNetCore.Hosting.HttpRequestIn</c> activity,
/// and exactly one is emitted per request. This registration adds the middleware (via
/// <see cref="IStartupFilter"/>) that writes onto it what the runtime leaves empty: the qyl domain,
/// the HTTP and URL conventions, the route and the span name, and the configured request and
/// response headers. Subscribe to the <c>Microsoft.AspNetCore</c> source to export it —
/// <c>AddQyl()</c> does both.
/// </remarks>
public static class QylAspNetCoreInstrumentationServiceCollectionExtensions
{
    /// <summary>
    /// Registers the qyl ASP.NET Core enrichment middleware. Idempotent — repeated calls keep a single
    /// registration.
    /// </summary>
    /// <param name="services">The application service collection.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddQylAspNetCoreInstrumentation(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        // IStartupFilters compose in registration order and this one must stay outermost, so the
        // request attributes are written before anything can short-circuit the pipeline and the
        // response status is read after everything else has run — call this before registering other
        // pipeline-wrapping filters.
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, QylAspNetCoreStartupFilter>());
        return services;
    }
}
