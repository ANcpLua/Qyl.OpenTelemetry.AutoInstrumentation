namespace Qyl.AutoInstrumentation.DiagnosticListeners.AspNetCore;

/// <summary>
/// Subscribes to <c>Microsoft.AspNetCore</c> — the listener emitted by Kestrel + the MVC pipeline.
/// Provides HTTP SERVER spans without any per-request middleware injection.
/// </summary>
public sealed class AspNetCoreDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Microsoft.AspNetCore";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        // Skeleton — HTTP SERVER span emission via QylActivitySource arrives next.
    }
}
