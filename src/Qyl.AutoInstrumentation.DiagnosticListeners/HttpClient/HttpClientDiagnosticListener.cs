namespace Qyl.AutoInstrumentation.DiagnosticListeners.HttpClient;

/// <summary>
/// Subscribes to <c>HttpHandlerDiagnosticListener</c> — the listener emitted by
/// <c>System.Net.Http.HttpClient</c>'s <see cref="System.Diagnostics.DiagnosticSource"/> integration.
/// AOT-native replacement for substrate's HttpClient CallTarget integration (M1 walking skeleton
/// in the substrate era was this exact span; the new M1 will re-prove it through this path).
/// </summary>
public sealed class HttpClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "HttpHandlerDiagnosticListener";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        // Skeleton: payload extraction + Activity emission via QylActivitySource arrives in the
        // next new-substrate milestone. The subscription itself proves the AOT path is wired.
    }
}
