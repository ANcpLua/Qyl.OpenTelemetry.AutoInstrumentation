using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>WCF client operation spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.WcfClient, QylAttributes.InstrumentationDomainValues.RpcWcfClient)]
[QylIntercept("System.ServiceModel.ClientBase`1", Shape = QylShapes.WcfClient, Start = nameof(Call))]
public static class QylInterceptedWcfClient
{
    // rpc.system.name enumerates only connectrpc, dubbo, grpc and jsonrpc; dotnet_wcf survives on
    // the deprecated rpc.system alone, so there is no generated member to read it from.
    private const string DotNetWcfSystemName = "dotnet_wcf";

    /// <summary>Starts the client span for the contract operation against the client's endpoint.</summary>
    public static Activity? Call([QylFromShape] string method, [QylFromReceiver("Endpoint?.Address?.Uri")] Uri? endpointUri)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.WcfClient,
            QylSpanNames.Rpc(method, DotNetWcfSystemName),
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.RpcWcfClient);
        if (activity is null)
            return null;

        QylActivityTags.SetRpc(
            activity,
            DotNetWcfSystemName,
            method);
        if (endpointUri is { IsAbsoluteUri: true })
        {
            activity.SetTag(ServerAttributes.Address, endpointUri.Host);
            if (!endpointUri.IsDefaultPort)
                activity.SetTag(ServerAttributes.Port, endpointUri.Port);
        }

        return activity;
    }
}
