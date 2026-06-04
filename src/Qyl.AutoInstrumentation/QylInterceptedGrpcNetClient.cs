using System.Diagnostics;
using Grpc.Core;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedGrpcNetClient
{
    private const string GrpcDomain = "rpc.grpc";

    public static Activity? StartActivity(string clientTypeName, string methodName, Metadata? requestMetadata)
    {
        ArgumentNullException.ThrowIfNull(clientTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GrpcNetClient))
            return null;

        var service = GetServiceName(clientTypeName);
        var activity = QylActivitySource.Source.StartActivity("gRPC " + service + "/" + methodName, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, GrpcDomain);
        activity.SetTag(QylSemanticAttributes.RpcSystem, "grpc");
        activity.SetTag(QylSemanticAttributes.RpcService, service);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
        SetConfiguredMetadata(activity, QylSemanticAttributes.GrpcRequestMetadataPrefix, QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadata, requestMetadata);
        return activity;
    }

    public static async Task<TResponse> ObserveUnaryResponseAsync<TResponse>(Task<TResponse> responseTask, Activity? activity)
    {
        if (activity is null)
            return await responseTask.ConfigureAwait(false);

        try
        {
            var response = await responseTask.ConfigureAwait(false);
            activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, 0);
            return response;
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            throw;
        }
        finally
        {
            activity.Dispose();
        }
    }

    public static async Task<Metadata> ObserveResponseHeadersAsync(Task<Metadata> responseHeadersTask, Activity? activity)
    {
        var metadata = await responseHeadersTask.ConfigureAwait(false);
        SetConfiguredMetadata(activity, QylSemanticAttributes.GrpcResponseMetadataPrefix, QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadata, metadata);
        return metadata;
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    public static void RecordStreamingComplete(Activity? activity)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, 0);
        activity.Dispose();
    }

    public static void Dispose(Activity? activity)
        => activity?.Dispose();

    private static void SetConfiguredMetadata(Activity? activity, string prefix, string[] configuredMetadata, Metadata? metadata)
    {
        if (activity is null || metadata is null || configuredMetadata.Length is 0)
            return;

        foreach (var metadataName in configuredMetadata)
        {
            var normalizedName = NormalizeMetadataName(metadataName);
            foreach (var entry in metadata)
            {
                if (entry.IsBinary ||
                    !string.Equals(entry.Key, normalizedName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                activity.SetTag(prefix + normalizedName, entry.Value);
                break;
            }
        }
    }

    private static string NormalizeMetadataName(string metadataName)
        => metadataName.Trim().ToLowerInvariant().Replace('_', '-');

    private static string GetServiceName(string clientTypeName)
    {
        var lastDot = clientTypeName.LastIndexOf(".", StringComparison.Ordinal);
        var service = lastDot < 0 ? clientTypeName : clientTypeName[(lastDot + 1)..];
        return service.EndsWith("Client", StringComparison.Ordinal) ? service[..^6] : service;
    }
}
