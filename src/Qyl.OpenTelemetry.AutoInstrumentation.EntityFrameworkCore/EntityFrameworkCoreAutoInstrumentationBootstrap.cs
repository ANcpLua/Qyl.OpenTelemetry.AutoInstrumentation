namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Package bootstrap surface used by the build-transitive consumer initializer.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class EntityFrameworkCoreAutoInstrumentationBootstrap
{
    /// <summary>Boot qyl EFCore auto-instrumentation for the current process.</summary>
    public static void Boot()
        => global::Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore.EntityFrameworkCoreModuleInitializerBoot.Boot();
}
