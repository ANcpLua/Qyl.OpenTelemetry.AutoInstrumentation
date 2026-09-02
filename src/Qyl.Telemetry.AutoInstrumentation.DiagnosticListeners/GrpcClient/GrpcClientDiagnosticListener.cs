using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

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
            QylSpanNames.Grpc(method),
            ActivityKind.Client);

        SemanticTagWriter.Set(activity, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.RpcGrpc);
        SemanticTagWriter.Set(activity, RpcAttributes.SystemName, RpcAttributes.SystemNameValues.Grpc);
        SemanticTagWriter.Set(activity, RpcAttributes.Method, method);
        SemanticTagWriter.Set(activity, RpcAttributes.MethodOriginal, originalMethod);
        SemanticTagWriter.Set(activity, ServerAttributes.Address, requestUri?.Host);
        SemanticTagWriter.Set(activity, ServerAttributes.Port, requestUri?.Port);
        if (requestUri is not null &&
            Uri.CheckHostName(requestUri.Host) is UriHostNameType.IPv4 or UriHostNameType.IPv6)
        {
            SemanticTagWriter.Set(activity, NetworkAttributes.PeerAddress, requestUri.Host);
            SemanticTagWriter.Set(activity, NetworkAttributes.PeerPort, requestUri.Port);
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
