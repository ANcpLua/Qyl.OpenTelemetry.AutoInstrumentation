using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>WCF client operation spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.WcfClient, QylAttributes.InstrumentationDomainValues.RpcWcfClient)]
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
            QylAttributes.InstrumentationDomainValues.RpcWcfClient);
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
