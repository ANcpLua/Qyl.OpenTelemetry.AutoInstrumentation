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
/// response headers plus the query string. Prefer this when you want the richer middleware attributes;
/// the zero-config <c>Qyl.OpenTelemetry.AutoInstrumentation.Hosting</c> module-init path produces server
/// spans via the ASP.NET Core <c>DiagnosticListener</c> instead and must not be combined with this one
/// (doing so would emit two server spans per request).
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
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IStartupFilter, QylAspNetCoreStartupFilter>());
        return services;
    }
}
