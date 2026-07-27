using System.Diagnostics;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Grpc.Core;
using Grpc.Net.Client;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.RealGrpcClientDemo;
using ErrorAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using NetworkAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using RpcAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Server.ServerAttributes;

var captured = new List<CapturedActivity>();
var capturedLock = new Lock();
var byteArrayMarshaller = new Marshaller<byte[]>(
    static value => value,
    static value => value);

using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            captured.Add(CapturedActivity.From(activity));
        }
    },
};

ActivitySource.AddActivityListener(listener);

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(static server =>
{
    server.Listen(IPAddress.Loopback, 0, static listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});
builder.Logging.ClearProviders();
builder.Services.AddHealthChecks();

var app = builder.Build();
app.MapHealthChecks("/healthz");
app.MapPost("/qyl.LiveProbe/Collect", static async context =>
{
    context.Response.ContentType = "application/grpc";
    context.Response.Headers["x-demo-res-md"] = "sv1";
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.AppendTrailer("grpc-status", "0");
    await context.Response.Body.WriteAsync(new byte[] { 0, 0, 0, 0, 0 });
});

await app.StartAsync();

var address = new Uri(app.Urls.Single());

// Two public call shapes, one telemetry owner. Both raw CallInvoker calls and the
// protoc-generated ClientBase<T> shape are completed by Grpc.Net.Client's native
// DiagnosticSource, which exposes the protocol method, channel address, and status.
var clientMode = string.Equals(
    Environment.GetEnvironmentVariable("QYL_GRPC_DEMO_MODE"), "client", StringComparison.OrdinalIgnoreCase);

try
{
    var method = new Method<byte[], byte[]>(
        MethodType.Unary,
        "qyl.LiveProbe",
        "Collect",
        byteArrayMarshaller,
        byteArrayMarshaller);

    using var channel = GrpcChannel.ForAddress(address);
    var requestMetadata = new Metadata { { "x-demo-md", "mv1" } };
    if (clientMode)
    {
        var client = new LiveProbeClient(channel);
        _ = await client.CollectAsync(Array.Empty<byte>(), requestMetadata);
    }
    else
    {
        _ = await channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(requestMetadata), Array.Empty<byte>());
    }

    try
    {
        using var failureChannel = GrpcChannel.ForAddress("http://127.0.0.1:1");
        if (clientMode)
        {
            var failureClient = new LiveProbeClient(failureChannel);
            _ = await failureClient.CollectAsync(Array.Empty<byte>());
        }
        else
        {
            _ = await failureChannel
                .CreateCallInvoker()
                .AsyncUnaryCall(method, null, new CallOptions(), Array.Empty<byte>());
        }
    }
    catch (RpcException exception)
    {
        Console.WriteLine($"expected-failure={exception.StatusCode}");
    }
}
finally
{
    await app.StopAsync();
}

var report = GrpcClientReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray(),
    address);

var json = JsonSerializer.Serialize(report, RealGrpcClientJsonContext.Default.GrpcClientReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => tag.Value switch
                {
                    string s => s,
                    System.Collections.IEnumerable e => string.Join(",", e.Cast<object?>()),
                    var other => Convert.ToString(other, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                },
                StringComparer.Ordinal));
}

internal sealed record GrpcClientReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    private const string RequestMetadata = RpcAttributes.RequestMetadata + ".x-demo-md";
    private const string ResponseMetadata = RpcAttributes.ResponseMetadata + ".x-demo-res-md";

    public static GrpcClientReport Create(string runtimeMode, CapturedActivity[] activities, Uri serverAddress)
    {
        var failures = new List<string>();
        var grpcSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "rpc.grpc"))
            .ToArray();

        if (grpcSpans.Length != 2)
            failures.Add($"expected 2 real gRPC client spans, got {grpcSpans.Length}");

        const string expectedMethod = "qyl.LiveProbe/Collect";

        var successSpan = FindByStatus(grpcSpans, "OK");
        var failureSpan = FindByStatus(grpcSpans, "UNAVAILABLE");

        Require(successSpan, "OK span", failures);
        Require(failureSpan, "failure span", failures);
        RequireTag(successSpan, RpcAttributes.SystemName, RpcAttributes.SystemNameValues.Grpc, failures);
        RequireTag(successSpan, RpcAttributes.Method, expectedMethod, failures);
        RequireTag(successSpan, RpcAttributes.ResponseStatusCode, "OK", failures);
        RequireTag(successSpan, ServerAttributes.Address, serverAddress.Host, failures);
        RequireTag(successSpan, ServerAttributes.Port, serverAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), failures);
        RequireTag(successSpan, NetworkAttributes.PeerAddress, serverAddress.Host, failures);
        RequireTag(successSpan, NetworkAttributes.PeerPort, serverAddress.Port.ToString(System.Globalization.CultureInfo.InvariantCulture), failures);
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Error", failures);
        RequireTag(failureSpan, RpcAttributes.SystemName, RpcAttributes.SystemNameValues.Grpc, failures);
        RequireTag(failureSpan, RpcAttributes.Method, expectedMethod, failures);
        RequireTag(failureSpan, RpcAttributes.ResponseStatusCode, "UNAVAILABLE", failures);
        RequireTag(failureSpan, ServerAttributes.Address, "127.0.0.1", failures);
        RequireTag(failureSpan, ServerAttributes.Port, "1", failures);
        RequireTag(failureSpan, NetworkAttributes.PeerAddress, "127.0.0.1", failures);
        RequireTag(failureSpan, NetworkAttributes.PeerPort, "1", failures);
        RequireTag(failureSpan, ErrorAttributes.Type, "UNAVAILABLE", failures);
        RequireMissingTag(successSpan, ErrorAttributes.Type, failures);

        // Metadata capture is asserted in both directions, keyed off the env vars
        // the runtime honors.
        var captureOptIn = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA"));
        if (captureOptIn)
        {
            RequireTag(successSpan, RequestMetadata, "mv1", failures);
            RequireTag(successSpan, ResponseMetadata, "sv1", failures);
        }
        else if (successSpan is not null &&
                 (successSpan.Tags.ContainsKey(RequestMetadata) ||
                  successSpan.Tags.ContainsKey(ResponseMetadata)))
        {
            failures.Add("gRPC metadata captured without opt-in");
        }

        foreach (var span in grpcSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Kind, nameof(ActivityKind.Client)))
                failures.Add($"expected gRPC Client span, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Name, expectedMethod))
                failures.Add($"unexpected gRPC span name: {span.Name}");
            RequireMissingTag(span, "rpc.service", failures);
            RequireMissingTag(span, "rpc.grpc.status_code", failures);
        }

        return new GrpcClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

    private static CapturedActivity? FindByStatus(IEnumerable<CapturedActivity> activities, string statusCode)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue(RpcAttributes.ResponseStatusCode, out var actual) &&
            StringComparer.Ordinal.Equals(actual, statusCode));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequireStatus(CapturedActivity? activity, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!StringComparer.Ordinal.Equals(activity.Status, expected))
            failures.Add($"expected span status {expected}, got {activity.Status}");
    }

    private static void RequireMissingTag(CapturedActivity? activity, string key, ICollection<string> failures)
    {
        if (activity?.Tags.ContainsKey(key) is true)
            failures.Add($"unexpected deprecated {key}");
    }
}

[JsonSerializable(typeof(GrpcClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealGrpcClientJsonContext : JsonSerializerContext;
