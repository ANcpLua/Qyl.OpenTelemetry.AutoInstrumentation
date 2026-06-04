using System.Diagnostics;
using Qyl.AutoInstrumentation;

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
        if (!StringComparer.Ordinal.Equals(name, "qyl.rpc.grpc") &&
            !StringComparer.Ordinal.Equals(name, "Grpc.Net.Client.GrpcOut.Stop"))
        {
            return;
        }

        var service = DiagnosticPayloadReader.GetString(payload, "rpc.service", "qyl.Greeter");
        var method = DiagnosticPayloadReader.GetString(payload, "rpc.method", "SayHello");
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "localhost");
        var serverPort = DiagnosticPayloadReader.GetInt32(payload, "server.port", 5001);

        using var activity = QylActivitySource.Source.StartActivity($"gRPC {service}/{method}", ActivityKind.Client);
        activity?.SetTag("qyl.instrumentation.domain", "rpc.grpc");
        activity?.SetTag("rpc.system", "grpc");
        activity?.SetTag("rpc.service", service);
        activity?.SetTag("rpc.method", method);
        activity?.SetTag("server.address", serverAddress);
        activity?.SetTag("server.port", serverPort);
    }
}
