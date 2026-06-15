using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Qyl.OpenTelemetry.AutoInstrumentation;

namespace Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore;

internal static class EntityFrameworkCoreModuleInitializerBoot
{
    private static int _booted;

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255",
        Justification = "Module-init is the AOT-native attach mechanism for the EFCore-specific qyl package.")]
    public static void Boot()
    {
        if (Interlocked.Exchange(ref _booted, 1) == 1)
            return;

        QylInstrumentation.Activate();
        new EntityFrameworkCoreDiagnosticListener().Subscribe();
    }
}
