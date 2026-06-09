using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Qyl.AutoInstrumentation.DiagnosticListeners.AspNetCore;
using Qyl.AutoInstrumentation.DiagnosticListeners.EntityFrameworkCore;
using Qyl.AutoInstrumentation.DiagnosticListeners.GrpcClient;
using Qyl.AutoInstrumentation.DiagnosticListeners.HttpClient;
using Qyl.AutoInstrumentation.DiagnosticListeners.SqlClient;

namespace Qyl.AutoInstrumentation.Hosting;

/// <summary>
/// AOT-native package bootstrap for applications that reference the hosting package.
///
/// <para>
/// When an app references <c>Qyl.AutoInstrumentation.Hosting</c>, the compiler emits a call to
/// <see cref="Boot"/> as part of the assembly's module-init sequence. The boot path is ordinary
/// compiled C# that NativeAOT understands natively.
/// </para>
///
/// <para>
/// The initializer subscribes one observer per DiagnosticListener channel and activates the qyl
/// ActivityListener. All sites it touches are AOT-safe and avoid dynamic discovery.
/// </para>
/// </summary>
internal static class ModuleInitializerBoot
{
    private static int _booted;

    /// <summary>The single qyl entry point invoked by the CLR at module load.</summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255",
        Justification = "Module-init IS the AOT-native attach mechanism this package ships; opt-in is the act of referencing Qyl.AutoInstrumentation.Hosting.")]
    public static void Boot()
    {
        if (Interlocked.Exchange(ref _booted, 1) == 1)
            return;

        QylInstrumentation.Activate();

        new HttpClientDiagnosticListener().Subscribe();
        new AspNetCoreDiagnosticListener().Subscribe();
        new EntityFrameworkCoreDiagnosticListener().Subscribe();
        new SqlClientDiagnosticListener().Subscribe();
        new GrpcClientDiagnosticListener().Subscribe();
    }
}
