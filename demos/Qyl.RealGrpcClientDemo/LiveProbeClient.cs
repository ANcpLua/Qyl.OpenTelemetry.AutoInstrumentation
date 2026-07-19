using Grpc.Core;

namespace Qyl.RealGrpcClientDemo;

/// <summary>
/// The protoc-generated client shape: a <see cref="ClientBase{T}"/> subclass whose call
/// methods return <see cref="AsyncUnaryCall{TResponse}"/>. Call sites on this type are
/// owned by the qyl source-interceptor lane.
/// </summary>
internal sealed class LiveProbeClient : ClientBase<LiveProbeClient>
{
    private static readonly Marshaller<byte[]> Bytes = new(static value => value, static value => value);
    private static readonly Method<byte[], byte[]> CollectMethod = new(
        MethodType.Unary, "qyl.LiveProbe", "Collect", Bytes, Bytes);
    private static readonly Method<byte[], byte[]> CollectStreamMethod = new(
        MethodType.ServerStreaming, "qyl.LiveProbe", "CollectStream", Bytes, Bytes);
    private static readonly Method<byte[], byte[]> CollectClientStreamMethod = new(
        MethodType.ClientStreaming, "qyl.LiveProbe", "CollectClientStream", Bytes, Bytes);
    private static readonly Method<byte[], byte[]> CollectDuplexMethod = new(
        MethodType.DuplexStreaming, "qyl.LiveProbe", "CollectDuplex", Bytes, Bytes);

    public LiveProbeClient(ChannelBase channel) : base(channel)
    {
    }

    private LiveProbeClient(ClientBaseConfiguration configuration) : base(configuration)
    {
    }

    public AsyncUnaryCall<byte[]> CollectAsync(byte[] request, Metadata? headers = null)
        => CallInvoker.AsyncUnaryCall(CollectMethod, host: null, new CallOptions(headers), request);

    public AsyncServerStreamingCall<byte[]> CollectStream(byte[] request, Metadata? headers = null)
        => CallInvoker.AsyncServerStreamingCall(CollectStreamMethod, host: null, new CallOptions(headers), request);

    public AsyncClientStreamingCall<byte[], byte[]> CollectClientStream(Metadata? headers = null)
        => CallInvoker.AsyncClientStreamingCall(CollectClientStreamMethod, host: null, new CallOptions(headers));

    public AsyncDuplexStreamingCall<byte[], byte[]> CollectDuplex(Metadata? headers = null)
        => CallInvoker.AsyncDuplexStreamingCall(CollectDuplexMethod, host: null, new CallOptions(headers));

    protected override LiveProbeClient NewInstance(ClientBaseConfiguration configuration)
        => new(configuration);
}
