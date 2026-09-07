using Microsoft.Extensions.DependencyInjection;
using OpenTelemetry.Trace;
using Qyl;
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
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;

// 15.0.0 hands the gRPC client lane to Grpc.Net.Client's own source, and the qyl domain is stamped
// onto those spans by the processor AddQyl registers. A bare ActivityListener on the qyl source
// therefore sees nothing, and one on the vendor source sees spans without the domain — so the demo
// reads what a consumer's collector would receive, out of the pipeline AddQyl builds.
var exported = new List<Activity>();
var byteArrayMarshaller = new Marshaller<byte[]>(
    static value => value,
    static value => value);

var builder = WebApplication.CreateSlimBuilder(args);
builder.WebHost.ConfigureKestrel(static server =>
{
    server.Listen(IPAddress.Loopback, 0, static listenOptions => listenOptions.Protocols = HttpProtocols.Http2);
});
builder.Logging.ClearProviders();
builder.Services.AddHealthChecks();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-grpc-client-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
});
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));

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
    exported.Select(CapturedActivity.From).ToArray(),
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
    private const string NativeSpanName = "Grpc.Net.Client.GrpcOut";
    private const string NativeMethodTag = "grpc.method";
    private const string NativeStatusTag = "grpc.status_code";
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

        // 15.0.0 stopped normalising this lane. The span is Grpc.Net.Client's own, so its shape is
        // the library's: the operation name it chose, `grpc.method` with a leading slash and the
        // numeric `grpc.status_code`, and none of the rpc.*, server.* or network.peer.* attributes
        // qyl used to synthesise. What is still qyl's is the domain, which is why the spans are
        // selected by it above. Request and response metadata are gone with the interceptor — a
        // processor never sees the gRPC call object — and the CHANGELOG records that as intentional.
        var successSpan = FindByStatus(grpcSpans, "0");
        var failureSpan = FindByStatus(grpcSpans, "14");

        Require(successSpan, "OK span", failures);
        Require(failureSpan, "failure span", failures);
        RequireTag(successSpan, NativeMethodTag, "/" + expectedMethod, failures);
        RequireTag(failureSpan, NativeMethodTag, "/" + expectedMethod, failures);
        // Both spans carry Unset: Grpc.Net.Client does not translate a non-zero grpc-status into an
        // Activity error status, where qyl's interceptor did. The failure is still visible, in
        // grpc.status_code=14, which is how the failing span is selected above — but a consumer
        // keying dashboards off span status will not see it any more.
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Unset", failures);

        foreach (var span in grpcSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Kind, nameof(ActivityKind.Client)))
                failures.Add($"expected gRPC Client span, got {span.Kind}");
            if (!StringComparer.Ordinal.Equals(span.Name, NativeSpanName))
                failures.Add($"unexpected gRPC span name: {span.Name}");
            RequireMissingTag(span, RequestMetadata, failures);
            RequireMissingTag(span, ResponseMetadata, failures);
        }

        return new GrpcClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

    private static CapturedActivity? FindByStatus(IEnumerable<CapturedActivity> activities, string statusCode)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue(NativeStatusTag, out var actual) &&
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
