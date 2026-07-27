using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.AspNetCore;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.GrpcClient;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.HttpClient;

namespace Qyl.Telemetry.AutoInstrumentation.Hosting;

/// <summary>
/// AOT-native package bootstrap for applications that reference the hosting package.
///
/// <para>
/// When an app references <c>Qyl.Telemetry.AutoInstrumentation.Hosting</c>, the compiler emits a call to
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
    private static readonly QylDiagnosticListenerSubscriber[] DiagnosticListeners =
    [
        new HttpClientDiagnosticListener(),
        new AspNetCoreDiagnosticListener(),
        new GrpcClientDiagnosticListener(),
    ];

    private static int _booted;

    /// <summary>The single qyl entry point invoked by the CLR at module load.</summary>
    [ModuleInitializer]
    [SuppressMessage("Usage", "CA2255",
        Justification = "Module-init IS the AOT-native attach mechanism this package ships; opt-in is the act of referencing Qyl.Telemetry.AutoInstrumentation.Hosting.")]
    public static void Boot()
    {
        if (Interlocked.Exchange(ref _booted, 1) == 1)
            return;

        QylInstrumentation.Activate();
        RegisterDiagnosticListeners();
    }

    private static void RegisterDiagnosticListeners()
    {
        foreach (var listener in DiagnosticListeners)
        {
            listener.Subscribe();
        }
    }
}
