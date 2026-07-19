using System.Diagnostics;
using Grpc.Core;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylRpcActivityPolicy
{
    public static Activity? StartGrpcClientActivity(string clientTypeName, string methodName, Metadata? requestMetadata)
    {
        ArgumentNullException.ThrowIfNull(clientTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        var service = GetGrpcServiceName(clientTypeName);
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.GrpcNetClient,
            QylActivityNames.GrpcClient(service, methodName),
            ActivityKind.Client,
            QylInstrumentationDomains.RpcGrpc);
        if (activity is null)
            return null;

        QylActivityTags.SetRpc(activity, QylSemanticAttributes.RpcSystemGrpc, service, methodName);
        QylCaptureHelpers.SetMetadataHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadataMap,
            requestMetadata);
        return activity;
    }

    public static void SetGrpcOkStatus(Activity activity)
        => activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, QylSemanticAttributes.RpcGrpcStatusCodeOk);

    public static void SetGrpcResponseMetadata(Activity? activity, Metadata? metadata)
        => QylCaptureHelpers.SetMetadataHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadataMap,
            metadata);

    private static string GetGrpcServiceName(string clientTypeName)
    {
        var lastDot = clientTypeName.LastIndexOf(".", StringComparison.Ordinal);
        var service = lastDot < 0 ? clientTypeName : clientTypeName[(lastDot + 1)..];
        return service.EndsWith("Client", StringComparison.Ordinal) ? service[..^6] : service;
    }
}
