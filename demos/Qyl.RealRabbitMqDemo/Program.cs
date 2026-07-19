using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Qyl.OpenTelemetry.AutoInstrumentation;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

var uriText = Environment.GetEnvironmentVariable("QYL_RABBITMQ_URI");
if (string.IsNullOrWhiteSpace(uriText))
{
    Console.Error.WriteLine("QYL_RABBITMQ_URI is required.");
    return 2;
}

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

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

await using (var failingChannel = await connection.CreateChannelAsync(channelOptions))
{
    try
    {
        await failingChannel.BasicPublishAsync("qyl.missing.exchange", "qyl", Encoding.UTF8.GetBytes("qyl-error"));
    }
    catch (RabbitMQClientException exception)
    {
        Console.WriteLine("expected-rabbitmq-error=" + exception.GetType().Name);
    }
}

var report = RabbitMqReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

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
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record RabbitMqReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    public static RabbitMqReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var rabbitSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "messaging.rabbitmq"))
            .ToArray();

        var success = rabbitSpans.Where(static span => StringComparer.Ordinal.Equals(span.Status, "Unset")).ToArray();
        var error = rabbitSpans.Where(static span => StringComparer.Ordinal.Equals(span.Status, "Error")).ToArray();

        if (success.Length != 1)
            failures.Add($"expected 1 successful RabbitMQ publish span, got {success.Length}");

        if (error.Length != 1)
            failures.Add($"expected 1 error RabbitMQ publish span, got {error.Length}");

        foreach (var span in rabbitSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "RabbitMQ publish"))
                failures.Add($"unexpected RabbitMQ span name: {span.Name}");

            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.System, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.SystemValues.Rabbitmq, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationType, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationTypeValues.Send, failures);
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationName, "publish", failures);

            if (!StringComparer.Ordinal.Equals(span.Kind, "Producer"))
                failures.Add($"expected kind Producer, got {span.Kind}");
        }

        foreach (var span in error)
            RequireTag(span, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(RabbitMQ.Client.Exceptions.AlreadyClosedException).FullName!, failures);

        return new RabbitMqReport(runtimeMode, failures.Count is 0, failures.ToArray(), rabbitSpans);
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
}

[JsonSerializable(typeof(RabbitMqReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealRabbitMqJsonContext : JsonSerializerContext;
