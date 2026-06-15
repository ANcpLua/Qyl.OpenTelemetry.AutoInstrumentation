namespace Qyl.OpenTelemetry.AutoInstrumentation.SqlClient;

/// <summary>Package bootstrap surface used by the build-transitive consumer initializer.</summary>
public static class SqlClientAutoInstrumentationBootstrap
{
    /// <summary>Boot qyl Microsoft.Data.SqlClient auto-instrumentation for the current process.</summary>
    public static void Boot()
        => SqlClientModuleInitializerBoot.Boot();
}
