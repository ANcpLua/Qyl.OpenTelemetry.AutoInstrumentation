using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.GrpcClient;

/// <summary>
/// Subscribes to <c>Grpc.Net.Client</c> — gRPC CLIENT spans without IL rewriting.
/// </summary>
public sealed class GrpcClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Grpc.Net.Client";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.GrpcNetClient;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.rpc.grpc") &&
            !StringComparer.Ordinal.Equals(name, "Grpc.Net.Client.GrpcOut.Stop"))
        {
            return;
        }

        var grpcMethod = DiagnosticPayloadReader.GetString(payload, "grpc.method");
        var service = DiagnosticPayloadReader.GetString(payload, "rpc.service") ??
                      RpcSemantics.GetService(grpcMethod);
        var method = DiagnosticPayloadReader.GetString(payload, "rpc.method") ??
                     RpcSemantics.GetMethod(grpcMethod);
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "peer.hostname");
        var serverPort = DiagnosticPayloadReader.GetInt32(payload, "server.port", "peer.port");
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, "rpc.grpc.status_code") ??
                         DiagnosticPayloadReader.GetInt32(payload, "grpc.status_code");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.Source.StartActivity("gRPC CLIENT", ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, "rpc.grpc");
        SemanticTagWriter.Set(activity, SemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemGrpc);
        SemanticTagWriter.Set(activity, SemanticAttributes.RpcService, service);
        SemanticTagWriter.Set(activity, SemanticAttributes.RpcMethod, method);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, serverAddress);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerPort, serverPort);
        RpcSemantics.SetGrpcStatus(activity, statusCode, errorType);
    }
}
