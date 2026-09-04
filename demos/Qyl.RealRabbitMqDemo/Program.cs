using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

var uriText = Environment.GetEnvironmentVariable("QYL_RABBITMQ_URI");
if (string.IsNullOrWhiteSpace(uriText))
{
    Console.Error.WriteLine("QYL_RABBITMQ_URI is required.");
    return 2;
}

// The real registration path: AddQyl subscribes to RabbitMQ.Client's own publisher and subscriber
// ActivitySources and installs the one native-span processor.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealRabbitMqDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-rabbitmq-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

var factory = new ConnectionFactory { Uri = new Uri(uriText) };
await using var connection = await WaitForRabbitMqAsync(factory);

var channelOptions = new CreateChannelOptions(
    publisherConfirmationsEnabled: true,
    publisherConfirmationTrackingEnabled: true);

await using (var channel = await connection.CreateChannelAsync(channelOptions))
{
    var queue = (await channel.QueueDeclareAsync()).QueueName;
    await channel.BasicPublishAsync(string.Empty, queue, Encoding.UTF8.GetBytes("qyl-publish"));
    Console.WriteLine("published-queue=" + queue);
}

await using (var namedChannel = await connection.CreateChannelAsync(channelOptions))
{
    await namedChannel.ExchangeDeclareAsync(RabbitMqReport.NamedExchange, ExchangeType.Fanout, autoDelete: true);
    await namedChannel.BasicPublishAsync(RabbitMqReport.NamedExchange, RabbitMqReport.NamedRoutingKey, Encoding.UTF8.GetBytes("qyl-exchange"));
    Console.WriteLine("published-exchange=" + RabbitMqReport.NamedExchange);
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = RabbitMqReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealRabbitMqJsonContext.Default.RabbitMqReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task<IConnection> WaitForRabbitMqAsync(ConnectionFactory factory)
{
    Exception? lastException = null;

    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            return await factory.CreateConnectionAsync();
        }
        catch (BrokerUnreachableException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("RabbitMQ did not become ready.", lastException);
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record RabbitMqReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    internal const string NamedExchange = "qyl-probe-exchange";
    internal const string NamedRoutingKey = "qyl";

    // RabbitMQ.Client's own name for the default exchange, which it substitutes for the empty one.
    private const string DefaultExchangeDestination = "amq.default";

    public static RabbitMqReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var publisherSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.RabbitMQClientPublisher))
            .ToArray();

        // One native span per publish, not per run.
        var defaultExchangeSpans = publisherSpans
            .Where(static span =>
                span.Tags.TryGetValue(MessagingAttributes.DestinationName, out var destination) &&
                StringComparer.Ordinal.Equals(destination, DefaultExchangeDestination))
            .ToArray();
        var namedExchangeSpans = publisherSpans
            .Where(static span =>
                span.Tags.TryGetValue(MessagingAttributes.DestinationName, out var destination) &&
                StringComparer.Ordinal.Equals(destination, NamedExchange))
            .ToArray();

        if (defaultExchangeSpans.Length != 1)
            failures.Add($"expected exactly 1 default-exchange publish span, got {defaultExchangeSpans.Length.ToString(CultureInfo.InvariantCulture)}");
        if (namedExchangeSpans.Length != 1)
            failures.Add($"expected exactly 1 '{NamedExchange}' publish span, got {namedExchangeSpans.Length.ToString(CultureInfo.InvariantCulture)}");
        if (publisherSpans.Length != 2)
            failures.Add($"expected exactly 2 RabbitMQ publisher spans, got {publisherSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in publisherSpans)
        {
            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.MessagingRabbitMq,
                failures);

            // RabbitMQ.Client's own attributes, which are the stable messaging conventions.
            RequireTag(span, MessagingAttributes.System, MessagingAttributes.SystemValues.Rabbitmq, failures);
            RequireTag(span, MessagingAttributes.OperationType, MessagingAttributes.OperationTypeValues.Send, failures);
            RequireTag(span, MessagingAttributes.OperationName, "publish", failures);
            RequirePresentTag(span, MessagingAttributes.RabbitmqDestinationRoutingKey, failures);
            RequirePresentTag(span, MessagingAttributes.MessageBodySize, failures);

            // RabbitMQTracingOptions.UseRoutingKeyAsOperationName defaults to true, so the library
            // names the span after the routing key as well as the operation.
            span.Tags.TryGetValue(MessagingAttributes.RabbitmqDestinationRoutingKey, out var routingKey);
            if (!StringComparer.Ordinal.Equals(span.Name, "publish " + routingKey))
                failures.Add($"unexpected RabbitMQ span name: {span.Name}");
            if (!StringComparer.Ordinal.Equals(span.Kind, "Producer"))
                failures.Add($"expected kind Producer, got {span.Kind}");
        }

        foreach (var span in namedExchangeSpans)
            RequireTag(span, MessagingAttributes.RabbitmqDestinationRoutingKey, NamedRoutingKey, failures);

        return new RabbitMqReport(runtimeMode, failures.Count is 0, failures.ToArray(), publisherSpans);
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key}");
    }
}

[JsonSerializable(typeof(RabbitMqReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealRabbitMqJsonContext : JsonSerializerContext;
