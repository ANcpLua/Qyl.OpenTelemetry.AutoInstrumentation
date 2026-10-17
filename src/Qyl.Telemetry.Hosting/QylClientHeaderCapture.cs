using System.Diagnostics;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Names;

namespace Qyl;

/// <summary>Enriches existing native HTTP and gRPC client activities at their synchronous diagnostic boundaries.</summary>
internal sealed partial class QylClientHeaderCapture : IDisposable,
    IObserver<DiagnosticListener>, IObserver<KeyValuePair<string, object?>>
{
    private const string HttpStart = "System.Net.Http.HttpRequestOut.Start";
    private const string HttpStop = "System.Net.Http.HttpRequestOut.Stop";
    private const string GrpcActivity = "Grpc.Net.Client.GrpcOut";
    private const string GrpcStart = GrpcActivity + ".Start";
    private const string GrpcStop = GrpcActivity + ".Stop";
    private readonly QylCapturedNameMap _requestHeaders;
    private readonly QylCapturedNameMap _responseHeaders;
    private readonly QylCapturedNameMap _requestMetadata;
    private readonly QylCapturedNameMap _responseMetadata;
    private readonly bool _httpEnabled;
    private readonly bool _grpcEnabled;
    private readonly ILogger _logger;
    private readonly Lock _gate = new();
    private readonly List<IDisposable> _events = [];
    private readonly IDisposable _listeners;
    private bool _disposed;

    internal QylClientHeaderCapture(QylAutoInstrumentationOptions options, ILogger logger)
    {
        _requestHeaders = options.HttpClientCapturedRequestHeaderMap;
        _responseHeaders = options.HttpClientCapturedResponseHeaderMap;
        _requestMetadata = options.GrpcCapturedRequestMetadataMap;
        _responseMetadata = options.GrpcCapturedResponseMetadataMap;
        _httpEnabled = options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.HttpClient) &&
            (_requestHeaders.Count > 0 || _responseHeaders.Count > 0);
        _grpcEnabled = options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.GrpcNetClient) &&
            (_requestMetadata.Count > 0 || _responseMetadata.Count > 0);
        _logger = logger;
        _listeners = DiagnosticListener.AllListeners.Subscribe(this);
    }

    void IObserver<DiagnosticListener>.OnNext(DiagnosticListener value)
    {
        if (!(value.Name == "HttpHandlerDiagnosticListener" && _httpEnabled || value.Name == QylTelemetryNames.VendorActivitySources.GrpcNetClient && _grpcEnabled)) return;
        lock (_gate)
        {
            if (!_disposed) _events.Add(value.Subscribe(this, IsEnabled));
        }
    }

    private bool IsEnabled(string name) => !Sdk.SuppressInstrumentation && name switch
    {
        HttpStart => _httpEnabled && _requestHeaders.Count > 0,
        HttpStop => _httpEnabled && _responseHeaders.Count > 0,
        // Grpc.Net.Client probes the base activity name before either diagnostic write.
        GrpcActivity or GrpcStart or GrpcStop => _grpcEnabled,
        _ => false
    };

    void IObserver<KeyValuePair<string, object?>>.OnNext(KeyValuePair<string, object?> value)
    {
        if (Sdk.SuppressInstrumentation || value.Value is not { } payload ||
            Activity.Current is not { IsAllDataRequested: true } activity) return;
        try
        {
            if (_httpEnabled && activity.Source.Name == QylFrameworkActivitySources.HttpClient &&
                activity.OperationName == "System.Net.Http.HttpRequestOut")
            {
                if (value.Key == HttpStart && _requestHeaders.Count > 0)
                    QylCaptureHelpers.SetHttpClientRequestHeaders(activity, _requestHeaders, HttpStartRequest(payload));
                else if (value.Key == HttpStop && _responseHeaders.Count > 0)
                    QylCaptureHelpers.SetHttpClientResponseHeaders(activity, _responseHeaders, HttpStopResponse(payload));
            }
            else if (_grpcEnabled && activity.Source.Name == QylTelemetryNames.VendorActivitySources.GrpcNetClient && activity.OperationName == GrpcActivity)
            {
                if (value.Key == GrpcStart && _requestMetadata.Count > 0)
                    QylCaptureHelpers.SetGrpcRequestMetadata(activity, _requestMetadata, GrpcStartRequest(payload));
                else if (value.Key == GrpcStop && _responseMetadata.Count > 0)
                    QylCaptureHelpers.SetGrpcResponseMetadata(activity, _responseMetadata, GrpcStopResponse(payload));
            }
        }
        catch (Exception exception)
        {
            // Instrumentation must not fail the request. Only a fixed category is logged;
            // payloads, header values and exception text never enter this diagnostic.
            ReportCaptureFailure(exception);
        }
    }

    private void ReportCaptureFailure(Exception exception) => CaptureFailed(_logger,
        exception is MissingMethodException or TypeLoadException ? "unsupported_payload" : "client_capture");

    void IObserver<DiagnosticListener>.OnError(Exception error) { }
    void IObserver<DiagnosticListener>.OnCompleted() { }
    void IObserver<KeyValuePair<string, object?>>.OnError(Exception error) { }
    void IObserver<KeyValuePair<string, object?>>.OnCompleted() { }

    public void Dispose()
    {
        IDisposable[] subscriptions;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            subscriptions = [.. _events];
            _events.Clear();
        }
        _listeners.Dispose();
        foreach (var subscription in subscriptions) subscription.Dispose();
    }

    // Fixed .NET 10 payload accessors, resolved by the runtime/AOT compiler rather than a
    // dynamic payload dispatcher. The diagnostic start is before propagation; stop is
    // before activity.Stop(). These private payload shapes are verified by the published consumer.
    // https://github.com/dotnet/runtime/blob/v10.0.12/src/libraries/System.Net.Http/src/System/Net/Http/DiagnosticsHandler.cs
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Request")]
    private static extern HttpRequestMessage HttpStartRequest(
        [UnsafeAccessorType("System.Net.Http.DiagnosticsHandler+ActivityStartData, System.Net.Http")] object payload);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Response")]
    private static extern HttpResponseMessage? HttpStopResponse(
        [UnsafeAccessorType("System.Net.Http.DiagnosticsHandler+ActivityStopData, System.Net.Http")] object payload);

    // Grpc.Net.Client 2.84.0 writes Stop synchronously in FinishCall before activity.Stop(),
    // after natural unary/stream completion makes available response trailers accessible.
    // https://github.com/grpc/grpc-dotnet/blob/4c6997a214601422dd66c5c74c1969db749479e9/src/Grpc.Net.Client/Internal/GrpcCall.cs
    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Request")]
    private static extern HttpRequestMessage GrpcStartRequest(
        [UnsafeAccessorType("Grpc.Net.Client.Internal.GrpcCall+ActivityStartData, Grpc.Net.Client")] object payload);

    [UnsafeAccessor(UnsafeAccessorKind.Method, Name = "get_Response")]
    private static extern HttpResponseMessage? GrpcStopResponse(
        [UnsafeAccessorType("Grpc.Net.Client.Internal.GrpcCall+ActivityStopData, Grpc.Net.Client")] object payload);

    [LoggerMessage(EventId = 1, Level = LogLevel.Warning,
        Message = "Native client header capture failed ({FailureCategory}); the request continues.")]
    private static partial void CaptureFailed(ILogger logger, string failureCategory);
}
