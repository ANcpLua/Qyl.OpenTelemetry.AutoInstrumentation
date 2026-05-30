using Microsoft.Extensions.DependencyInjection;

namespace Qyl.AutoInstrumentation.Hosting;

/// <summary>
/// Explicit-activation surface for apps that want a deterministic boot point inside their
/// <c>Program.cs</c> (rather than relying on <see cref="ModuleInitializerBoot"/>).
///
/// <para>
/// Both paths are equivalent — <see cref="AddQylAutoInstrumentation"/> just calls
/// <see cref="QylInstrumentation.Activate"/>, which is idempotent. Use this overload if you need
/// the call to appear in the app's source for compliance/audit reasons, or to compose with the
/// rest of the <c>IServiceCollection</c> pipeline.
/// </para>
/// </summary>
public static class QylAutoInstrumentationServiceCollectionExtensions
{
    /// <summary>Idempotently activate qyl auto-instrumentation for the current process.</summary>
    public static IServiceCollection AddQylAutoInstrumentation(this IServiceCollection services)
    {
        QylInstrumentation.Activate();
        return services;
    }
}
