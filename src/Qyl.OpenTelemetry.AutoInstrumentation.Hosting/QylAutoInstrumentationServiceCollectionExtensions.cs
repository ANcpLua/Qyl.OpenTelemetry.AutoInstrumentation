using Microsoft.Extensions.DependencyInjection;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Hosting;

/// <summary>
/// Explicit-activation surface for apps that want a deterministic boot point inside their
/// <c>Program.cs</c> (rather than relying on <see cref="ModuleInitializerBoot"/>).
///
/// <para>
/// Both paths are equivalent — <see cref="AddQylAutoInstrumentation"/> calls the same idempotent
/// bootstrap as the module initializer. Use this overload if you need the call to appear in the
/// app's source for compliance/audit reasons, or to compose with the rest of the
/// <c>IServiceCollection</c> pipeline.
/// </para>
/// </summary>
public static class QylAutoInstrumentationServiceCollectionExtensions
{
    /// <summary>Idempotently activate qyl auto-instrumentation for the current process.</summary>
    public static IServiceCollection AddQylAutoInstrumentation(this IServiceCollection services)
    {
        QylAutoInstrumentationBootstrap.Boot();
        return services;
    }

    /// <summary>Idempotently activate qyl auto-instrumentation with explicit hosting options.</summary>
    public static IServiceCollection AddQylAutoInstrumentation(
        this IServiceCollection services,
        Action<QylAutoInstrumentationHostingOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);

        var options = new QylAutoInstrumentationHostingOptions();
        configure(options);
        if (options.EnableConformanceProcessor)
            SemConvConformanceProcessor.Enable();

        QylAutoInstrumentationBootstrap.Boot();
        return services;
    }
}

/// <summary>Options for explicit qyl hosting activation.</summary>
public sealed class QylAutoInstrumentationHostingOptions
{
    /// <summary>Enable the development-only semconv conformance counter.</summary>
    public bool EnableConformanceProcessor { get; set; }
}
