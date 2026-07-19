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
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.RealGrpcClientDemo;

var captured = new List<CapturedActivity>();
var byteArrayMarshaller = new Marshaller<byte[]>(
    static value => value,
    static value => value);

using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
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

// Two lanes, one binary. "invoker" (default) drives raw CallInvoker calls, which the
// interceptor cannot see — the DiagnosticListeners lane emits. "client" drives a
// ClientBase<T>-derived client (the protoc-generated shape); the source interceptor
// owns the signal and the listener defers, so metadata capture is provable there.
var clientMode = string.Equals(
    Environment.GetEnvironmentVariable("QYL_GRPC_DEMO_MODE"), "client", StringComparison.OrdinalIgnoreCase);

try
{
    var address = app.Urls.Single();
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
    clientMode);

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
    public static GrpcClientReport Create(string runtimeMode, CapturedActivity[] activities, bool clientMode)
    {
        var failures = new List<string>();
        var grpcSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "rpc.grpc"))
            .ToArray();

        if (grpcSpans.Length != 2)
            failures.Add($"expected 2 real gRPC client spans, got {grpcSpans.Length}");

        var expectedService = clientMode ? "LiveProbe" : "qyl.LiveProbe";
        var expectedMethod = clientMode ? "CollectAsync" : "Collect";
        var expectedName = $"{expectedService}/{expectedMethod}";

        var successSpan = FindByStatus(grpcSpans, "0");
        var failureSpan = grpcSpans.FirstOrDefault(static activity =>
            StringComparer.Ordinal.Equals(activity.Status, "Error"));

        Require(successSpan, "OK span", failures);
        Require(failureSpan, "failure span", failures);
        RequireTag(successSpan, "rpc.system.name", "grpc", failures);
        RequireTag(successSpan, "rpc.service", expectedService, failures);
        RequireTag(successSpan, "rpc.method", expectedMethod, failures);
        RequireTag(successSpan, "rpc.grpc.status_code", "0", failures);
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Error", failures);
        if (!clientMode)
        {
            RequireTag(failureSpan, "rpc.grpc.status_code", "14", failures);
            RequireTag(failureSpan, "error.type", "14", failures);
        }

        // Metadata capture is asserted in both directions, keyed off the env vars
        // the runtime honors. The interceptor lane (client mode) is the one that
        // owns capture; the listener lane must never capture.
        var captureOptIn = !string.IsNullOrEmpty(
            Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA"));
        if (clientMode && captureOptIn)
        {
            RequireTag(successSpan, "rpc.request.metadata.x-demo-md", "mv1", failures);
            RequireTag(successSpan, "rpc.response.metadata.x-demo-res-md", "sv1", failures);
        }
        else if (successSpan is not null &&
                 (successSpan.Tags.ContainsKey("rpc.request.metadata.x-demo-md") ||
                  successSpan.Tags.ContainsKey("rpc.response.metadata.x-demo-res-md")))
        {
            failures.Add("gRPC metadata captured without opt-in");
        }

        foreach (var span in grpcSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, expectedName))
                failures.Add($"unexpected gRPC span name: {span.Name}");
        }

        return new GrpcClientReport(runtimeMode, failures.Count is 0, failures.ToArray(), activities);
    }

    private static CapturedActivity? FindByStatus(IEnumerable<CapturedActivity> activities, string statusCode)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue("rpc.grpc.status_code", out var actual) &&
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
}

[JsonSerializable(typeof(GrpcClientReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealGrpcClientJsonContext : JsonSerializerContext;
