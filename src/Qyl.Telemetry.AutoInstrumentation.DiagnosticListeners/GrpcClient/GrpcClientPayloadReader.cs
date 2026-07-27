using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Net.Http;
using System.Reflection;

namespace Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.GrpcClient;

internal static class GrpcClientPayloadReader
{
    private static readonly ConcurrentDictionary<Type, PayloadAccessor> PayloadAccessors = new();

    public static (HttpRequestMessage? Request, HttpResponseMessage? Response) GetMessages(object? payload)
    {
        if (payload is null)
            return default;

        var accessor = PayloadAccessors.GetOrAdd(payload.GetType(), CreatePayloadAccessor);
        var response = accessor.GetResponse(payload);
        return (accessor.GetRequest(payload) ?? response?.RequestMessage, response);
    }

    private static PayloadAccessor CreatePayloadAccessor(Type payloadType)
        => new(
            GetProperty<HttpRequestMessage>(payloadType, "Request"),
            GetProperty<HttpResponseMessage>(payloadType, "Response"));

    [UnconditionalSuppressMessage(
        "Trimming",
        "IL2075",
        Justification = "Grpc.Net.Client preserves the public properties on its DiagnosticSource payload types.")]
    private static PropertyInfo? GetProperty<T>(Type payloadType, string name)
    {
        var property = payloadType.GetProperty(name, BindingFlags.Instance | BindingFlags.Public);
        return property is { CanRead: true } && typeof(T).IsAssignableFrom(property.PropertyType)
            ? property
            : null;
    }

    private sealed class PayloadAccessor(PropertyInfo? requestProperty, PropertyInfo? responseProperty)
    {
        public HttpRequestMessage? GetRequest(object payload)
            => requestProperty?.GetValue(payload) as HttpRequestMessage;

        public HttpResponseMessage? GetResponse(object payload)
            => responseProperty?.GetValue(payload) as HttpResponseMessage;
    }
}
