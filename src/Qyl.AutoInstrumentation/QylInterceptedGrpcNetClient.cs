using System.Diagnostics;
using Grpc.Core;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted gRPC Net Client.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedGrpcNetClient);</code></example>
public static class QylInterceptedGrpcNetClient
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string clientTypeName, string methodName, Metadata? requestMetadata)
    {
        ArgumentNullException.ThrowIfNull(clientTypeName);
        ArgumentNullException.ThrowIfNull(methodName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GrpcNetClient))
            return null;

        var service = GetServiceName(clientTypeName);
        var activity = QylActivitySource.StartActivity(QylActivityNames.GrpcClient(service, methodName), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.RpcGrpc);
        activity.SetTag(QylSemanticAttributes.RpcSystem, QylSemanticAttributes.RpcSystemGrpc);
        activity.SetTag(QylSemanticAttributes.RpcService, service);
        activity.SetTag(QylSemanticAttributes.RpcMethod, methodName);
        QylCaptureHelpers.SetMetadataHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadataMap,
            requestMetadata);
        return activity;
    }

    /// <summary>Observes an asynchronous gRPC unary response and records qyl success, exception, and response metadata telemetry.</summary>
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

    /// <summary>Runs the Observe Response Headers Async runtime helper used by source-generated qyl interceptors.</summary>
    public static async Task<Metadata> ObserveResponseHeadersAsync(Task<Metadata> responseHeadersTask, Activity? activity)
    {
        var metadata = await responseHeadersTask.ConfigureAwait(false);
        SetResponseMetadata(activity, metadata);
        return metadata;
    }

    /// <summary>Runs the Capture Completed Response Headers runtime helper used by source-generated qyl interceptors.</summary>
    public static void CaptureCompletedResponseHeaders(Task<Metadata>? responseHeadersTask, Activity? activity)
    {
        if (activity is null || responseHeadersTask is null || !responseHeadersTask.IsCompletedSuccessfully)
            return;

        SetResponseMetadata(activity, responseHeadersTask.Result);
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }

    /// <summary>Runs the Record Streaming Complete runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordStreamingComplete(Activity? activity)
    {
        if (activity is null)
            return;

        activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, QylSemanticAttributes.RpcGrpcStatusCodeOk);
        activity.Dispose();
    }

    /// <summary>Runs the Dispose runtime helper used by source-generated qyl interceptors.</summary>
    public static void Dispose(Activity? activity)
        => activity?.Dispose();

    private static void SetResponseMetadata(Activity? activity, Metadata? metadata)
        => QylCaptureHelpers.SetMetadataHeaders(
            activity,
            QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadataMap,
            metadata);

    private static string GetServiceName(string clientTypeName)
    {
        var lastDot = clientTypeName.LastIndexOf(".", StringComparison.Ordinal);
        var service = lastDot < 0 ? clientTypeName : clientTypeName[(lastDot + 1)..];
        return service.EndsWith("Client", StringComparison.Ordinal) ? service[..^6] : service;
    }
}
