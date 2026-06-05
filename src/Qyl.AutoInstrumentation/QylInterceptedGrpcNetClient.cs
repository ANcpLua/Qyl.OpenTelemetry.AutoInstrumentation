using System.Diagnostics;
using Grpc.Core;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedGrpcNetClient
{

    public static Activity? StartActivity(string clientTypeName, string methodName, Metadata? requestMetadata)
    {
        ArgumentNullException.ThrowIfNull(clientTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GrpcNetClient))
            return null;

        var service = GetServiceName(clientTypeName);
        var activity = QylActivitySource.StartActivity("gRPC CLIENT", ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.RpcGrpc);
        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemGrpc);
        activity.SetTag(QylSemanticAttributes.RpcService, service);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
        SetConfiguredMetadata(activity, QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadataMap, requestMetadata);
        return activity;
    }

    public static async Task<TResponse> ObserveUnaryResponseAsync<TResponse>(
        Task<TResponse> responseTask,
        Task<Metadata> responseHeadersTask,
        Activity? activity)
    {
        if (activity is null)
            return await responseTask.ConfigureAwait(false);

        try
        {
            var response = await responseTask.ConfigureAwait(false);
            CaptureCompletedResponseHeaders(responseHeadersTask, activity);
            activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, QylSemanticAttributes.RpcGrpcStatusCodeOk);
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
        SetResponseMetadata(activity, metadata);
        return metadata;
    }

    public static void CaptureCompletedResponseHeaders(Task<Metadata>? responseHeadersTask, Activity? activity)
    {
        if (activity is null || responseHeadersTask is null || !responseHeadersTask.IsCompletedSuccessfully)
            return;

        SetResponseMetadata(activity, responseHeadersTask.Result);
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

        activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, QylSemanticAttributes.RpcGrpcStatusCodeOk);
        activity.Dispose();
    }

    public static void Dispose(Activity? activity)
        => activity?.Dispose();

    private static void SetResponseMetadata(Activity? activity, Metadata? metadata)
        => SetConfiguredMetadata(activity, QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadataMap, metadata);

    private static void SetConfiguredMetadata(Activity? activity, QylCapturedNameMap configuredMetadata, Metadata? metadata)
    {
        if (activity is null || metadata is null || configuredMetadata.Count is 0)
            return;

        for (var index = 0; index < configuredMetadata.Count; index++)
        {
            var lookupName = configuredMetadata.GetLookupName(index);
            List<string>? values = null;
            foreach (var entry in metadata)
            {
                if (entry.IsBinary ||
                    !string.Equals(entry.Key, lookupName, StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                (values ??= []).Add(entry.Value);
            }

            if (values is { Count: > 0 })
                activity.SetTag(configuredMetadata.GetTagName(index), values.Count is 1 ? values[0] : values.ToArray());
        }
    }

    private static string GetServiceName(string clientTypeName)
    {
        var lastDot = clientTypeName.LastIndexOf(".", StringComparison.Ordinal);
        var service = lastDot < 0 ? clientTypeName : clientTypeName[(lastDot + 1)..];
        return service.EndsWith("Client", StringComparison.Ordinal) ? service[..^6] : service;
    }
}
