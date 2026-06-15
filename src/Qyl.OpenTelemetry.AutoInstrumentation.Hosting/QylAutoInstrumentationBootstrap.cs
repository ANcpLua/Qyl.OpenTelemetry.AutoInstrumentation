namespace Qyl.OpenTelemetry.AutoInstrumentation.Hosting;

/// <summary>
/// Package bootstrap surface used by the build-transitive consumer initializer and by explicit
/// hosting integrations. The method is idempotent through <see cref="ModuleInitializerBoot"/>.
/// </summary>
public static class QylAutoInstrumentationBootstrap
{
    /// <summary>Boot qyl auto-instrumentation for the current process.</summary>
    public static void Boot()
        => ModuleInitializerBoot.Boot();
}
