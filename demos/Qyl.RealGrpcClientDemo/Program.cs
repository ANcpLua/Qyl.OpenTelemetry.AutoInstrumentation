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
    context.Response.StatusCode = StatusCodes.Status200OK;
    context.Response.AppendTrailer("grpc-status", "0");
    await context.Response.Body.WriteAsync(new byte[] { 0, 0, 0, 0, 0 });
});

await app.StartAsync();

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
    _ = await channel.CreateCallInvoker().AsyncUnaryCall(method, null, new CallOptions(), Array.Empty<byte>());

    try
    {
        using var failureChannel = GrpcChannel.ForAddress("http://127.0.0.1:1");
        _ = await failureChannel
            .CreateCallInvoker()
            .AsyncUnaryCall(method, null, new CallOptions(), Array.Empty<byte>());
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
    captured.ToArray());

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
                static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record GrpcClientReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static GrpcClientReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var grpcSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "rpc.grpc"))
            .ToArray();

        if (grpcSpans.Length != 2)
            failures.Add($"expected 2 real gRPC client spans, got {grpcSpans.Length}");

        var successSpan = FindByStatus(grpcSpans, "0");
        var failureSpan = FindByStatus(grpcSpans, "14");

        Require(successSpan, "OK span", failures);
        Require(failureSpan, "Unavailable span", failures);
        RequireTag(successSpan, "rpc.system.name", "grpc", failures);
        RequireTag(successSpan, "rpc.service", "qyl.LiveProbe", failures);
        RequireTag(successSpan, "rpc.method", "Collect", failures);
        RequireTag(successSpan, "rpc.grpc.status_code", "0", failures);
        RequireTag(failureSpan, "rpc.grpc.status_code", "14", failures);
        RequireTag(failureSpan, "error.type", "14", failures);
        RequireStatus(successSpan, "Unset", failures);
        RequireStatus(failureSpan, "Error", failures);

        foreach (var span in grpcSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "qyl.LiveProbe/Collect"))
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
