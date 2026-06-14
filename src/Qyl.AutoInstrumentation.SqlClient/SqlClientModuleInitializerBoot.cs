using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Qyl.AutoInstrumentation;

namespace Qyl.AutoInstrumentation.SqlClient;

internal static class SqlClientModuleInitializerBoot
{
    private static int _booted;

    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255",
        Justification = "Module-init is the AOT-native attach mechanism for the SqlClient-specific qyl package.")]
    public static void Boot()
    {
        if (Interlocked.Exchange(ref _booted, 1) == 1)
            return;

        QylInstrumentation.Activate();
        new SqlClientDiagnosticListener().Subscribe();
    }
}
