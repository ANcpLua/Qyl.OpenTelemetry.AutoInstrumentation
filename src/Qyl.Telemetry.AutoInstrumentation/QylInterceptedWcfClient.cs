using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>WCF client operation spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.WcfClient, QylInstrumentationDomains.RpcWcfClient)]
[QylIntercept("System.ServiceModel.ClientBase`1", Shape = QylShapes.WcfClient, Start = nameof(Call))]
public static class QylInterceptedWcfClient
{
    /// <summary>Starts the client span for the contract operation against the client's endpoint.</summary>
    public static Activity? Call([QylFromShape] string method, [QylFromReceiver("Endpoint?.Address?.Uri")] Uri? endpointUri)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.WcfClient,
            QylSpanNames.Rpc(method, QylSemanticAttributes.RpcSystemDotNetWcf),
            ActivityKind.Client,
            QylInstrumentationDomains.RpcWcfClient);
        if (activity is null)
            return null;

        QylActivityTags.SetRpc(
            activity,
            QylSemanticAttributes.RpcSystemDotNetWcf,
            method);
        if (endpointUri is { IsAbsoluteUri: true })
        {
            activity.SetTag(QylSemanticAttributes.ServerAddress, endpointUri.Host);
            if (!endpointUri.IsDefaultPort)
                activity.SetTag(QylSemanticAttributes.ServerPort, endpointUri.Port);
        }

        return activity;
    }
}
