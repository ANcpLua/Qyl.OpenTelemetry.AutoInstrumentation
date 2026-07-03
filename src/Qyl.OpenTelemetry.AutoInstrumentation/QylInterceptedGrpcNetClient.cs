using System.Diagnostics;
using Grpc.Core;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted gRPC Net Client.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylInterceptedGrpcNetClient
{
    // Registered on first use (inside an intercepted gRPC call, before the underlying call raises the
    // Grpc.Net.Client DiagnosticListener event) so the listener lane defers — no double-count.
    static QylInterceptedGrpcNetClient()
        => QylSignalOwnership.Register(QylAutoInstrumentationIds.GrpcNetClient, QylSignalOwnership.Interceptor);

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string clientTypeName, string methodName, Metadata? requestMetadata)
        => QylRpcActivityPolicy.StartGrpcClientActivity(clientTypeName, methodName, requestMetadata);

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
            QylRpcActivityPolicy.SetGrpcOkStatus(activity);
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

        QylRpcActivityPolicy.SetGrpcOkStatus(activity);
        activity.Dispose();
    }

    /// <summary>Runs the Dispose runtime helper used by source-generated qyl interceptors.</summary>
    public static void Dispose(Activity? activity)
        => activity?.Dispose();

    private static void SetResponseMetadata(Activity? activity, Metadata? metadata)
        => QylRpcActivityPolicy.SetGrpcResponseMetadata(activity, metadata);
}
