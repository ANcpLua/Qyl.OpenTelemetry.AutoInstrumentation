namespace Qyl.AutoInstrumentation.DiagnosticListeners.EntityFrameworkCore;

/// <summary>
/// Subscribes to <c>Microsoft.EntityFrameworkCore</c> — covers <c>CommandExecuting</c>,
/// <c>CommandExecuted</c>, <c>CommandError</c> across every DB provider EFCore supports.
/// Replaces the substrate's per-EFCore-version CallTarget integration.
/// </summary>
public sealed class EntityFrameworkCoreDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Microsoft.EntityFrameworkCore";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        // Skeleton — DB CLIENT span emission arrives next.
    }
}
