namespace Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore;

/// <summary>Package bootstrap surface used by the build-transitive consumer initializer.</summary>
public static class EntityFrameworkCoreAutoInstrumentationBootstrap
{
    /// <summary>Boot qyl EFCore auto-instrumentation for the current process.</summary>
    public static void Boot()
        => EntityFrameworkCoreModuleInitializerBoot.Boot();
}
