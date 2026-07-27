using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.GrpcClient;

/// <summary>
/// Subscribes to <c>Grpc.Net.Client</c> — gRPC CLIENT spans without IL rewriting.
/// </summary>
internal sealed class GrpcClientDiagnosticListener : QylDiagnosticListenerSubscriber
{
    private const string StopEventName = "Grpc.Net.Client.GrpcOut.Stop";
    private const string GrpcMethodTagName = "grpc.method";
    private const string GrpcStatusCodeTagName = "grpc.status_code";

    /// <inheritdoc/>
    protected override string ListenerName => "Grpc.Net.Client";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.GrpcNetClient;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, StopEventName))
            return;

        var method = QylGrpcSemantics.NormalizeMethod(
            DiagnosticPayloadReader.GetString(payload, GrpcMethodTagName),
            out var originalMethod);
        var statusCode = DiagnosticPayloadReader.GetInt32(payload, GrpcStatusCodeTagName);
        var (request, response) = GrpcClientPayloadReader.GetMessages(payload);
        var requestUri = request?.RequestUri;

        using var activity = QylActivitySource.StartAtAmbientStart(
            QylActivityNames.GrpcClient(method),
            ActivityKind.Client);

        SemanticTagWriter.Set(activity, QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.RpcGrpc);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemGrpc);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.RpcMethod, method);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.RpcMethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.ServerAddress, requestUri?.Host);
        SemanticTagWriter.Set(activity, QylSemanticAttributes.ServerPort, requestUri?.Port);
        if (requestUri is not null &&
            Uri.CheckHostName(requestUri.Host) is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            SemanticTagWriter.Set(activity, QylSemanticAttributes.NetworkPeerAddress, requestUri.Host);
            SemanticTagWriter.Set(activity, QylSemanticAttributes.NetworkPeerPort, requestUri.Port);
        }

        QylCaptureHelpers.SetHttpHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadataMap,
            request?.Headers,
            request?.Content?.Headers);
        QylCaptureHelpers.SetHttpHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadataMap,
            response?.Headers,
            response?.TrailingHeaders,
            response?.Content?.Headers);
        QylGrpcSemantics.SetStatus(activity, statusCode);
    }
}
