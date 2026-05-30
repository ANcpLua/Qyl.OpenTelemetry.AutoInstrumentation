namespace Qyl.AutoInstrumentation.DiagnosticListeners.GrpcClient;

/// <summary>
/// Subscribes to <c>Grpc.Net.Client</c> — gRPC CLIENT spans without IL rewriting.
/// </summary>
public sealed class GrpcClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Grpc.Net.Client";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        // Skeleton — gRPC CLIENT span emission arrives next.
    }
}
