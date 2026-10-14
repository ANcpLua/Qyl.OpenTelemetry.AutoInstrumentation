using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Confluent.Kafka;
using Confluent.Kafka.Admin;
using Qyl.Telemetry.AutoInstrumentation;

var bootstrapServers = Environment.GetEnvironmentVariable("QYL_KAFKA_BOOTSTRAP_SERVERS");
if (string.IsNullOrWhiteSpace(bootstrapServers))
{
    Console.Error.WriteLine("QYL_KAFKA_BOOTSTRAP_SERVERS is required.");
    return 2;
}

const string topic = "qyl-probe";

var captured = new List<CapturedActivity>();
var capturedLock = new Lock();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
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

// The same spans over OTLP to the collector QYL_ENDPOINT names, which the Demos lane reads them back from.
using var tracerProvider = QylDemoOtlp.CreateTracerProvider("qyl-real-kafka-demo", "Qyl.Telemetry.AutoInstrumentation");

await WaitForKafkaAsync(bootstrapServers, topic);

using (var producer = new ProducerBuilder<string, string>(WithSasl(new ProducerConfig
    {
        BootstrapServers = bootstrapServers,
        MessageTimeoutMs = 15000,
    }))
    .SetErrorHandler(static (_, _) => { })
    .SetLogHandler(static (_, _) => { })
    .Build())
{
    var delivery = await producer.ProduceAsync(topic, new Message<string, string> { Key = "alpha", Value = "qyl-async" });
    Console.WriteLine("produced-offset=" + delivery.Offset.Value.ToString(CultureInfo.InvariantCulture));

    producer.Produce(topic, new Message<string, string> { Key = "beta", Value = "qyl-sync" });
    producer.Flush(TimeSpan.FromSeconds(15));
}

using (var consumer = new ConsumerBuilder<string, string>(WithSasl(new ConsumerConfig
    {
        BootstrapServers = bootstrapServers,
        GroupId = "qyl-probe-consumer",
        AutoOffsetReset = AutoOffsetReset.Earliest,
        EnableAutoCommit = false,
    }))
    .SetErrorHandler(static (_, _) => { })
    .SetLogHandler(static (_, _) => { })
    .Build())
{
    consumer.Subscribe(topic);

    var received = 0;
    for (var attempt = 0; attempt < 60 && received < 2; attempt++)
    {
        var result = consumer.Consume(TimeSpan.FromSeconds(1));
        if (result?.Message is not null)
            received++;
    }

    consumer.Close();
    Console.WriteLine("consumed-messages=" + received.ToString(CultureInfo.InvariantCulture));
}

var expectedFailureMessage = string.Empty;
try
{
    using var failingProducer = new ProducerBuilder<string, string>(new ProducerConfig
        {
            BootstrapServers = "127.0.0.1:1",
            MessageTimeoutMs = 1500,
            SocketTimeoutMs = 1000,
            ApiVersionRequestTimeoutMs = 1000,
        })
        .SetErrorHandler(static (_, _) => { })
        .SetLogHandler(static (_, _) => { })
        .Build();

    _ = await failingProducer.ProduceAsync(topic, new Message<string, string> { Key = "gamma", Value = "qyl-error" });
}
catch (ProduceException<string, string> exception)
{
    expectedFailureMessage = exception.Message;
    Console.WriteLine("expected-kafka-error=" + exception.Error.Code);
}

QylDemoOtlp.Flush(tracerProvider);

var report = KafkaReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray(),
    expectedFailureMessage);

var json = JsonSerializer.Serialize(report, RealKafkaJsonContext.Default.KafkaReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task WaitForKafkaAsync(string bootstrapServers, string topic)
{
    using var admin = new AdminClientBuilder(WithSasl(new AdminClientConfig { BootstrapServers = bootstrapServers }))
        .SetErrorHandler(static (_, _) => { })
        .SetLogHandler(static (_, _) => { })
        .Build();

    Exception? lastException = null;
    for (var attempt = 0; attempt < 60; attempt++)
    {
        try
        {
            var metadata = admin.GetMetadata(TimeSpan.FromSeconds(2));
            if (metadata.Brokers.Count > 0)
            {
                await EnsureTopicAsync(admin, topic);
                return;
            }
        }
        catch (KafkaException exception)
        {
            lastException = exception;
        }

        await Task.Delay(TimeSpan.FromSeconds(1));
    }

    throw new InvalidOperationException("Kafka did not become ready.", lastException);
}

// The testbed broker authenticates with SASL/PLAIN over its TCP proxy. Unset credentials keep the
// plaintext connection a local broker accepts. The deliberately failing producer above connects
// nowhere and takes no credentials.
static T WithSasl<T>(T config)
    where T : ClientConfig
{
    var username = Environment.GetEnvironmentVariable("QYL_KAFKA_SASL_USERNAME");
    var password = Environment.GetEnvironmentVariable("QYL_KAFKA_SASL_PASSWORD");
    if (string.IsNullOrEmpty(username) || string.IsNullOrEmpty(password))
        return config;

    config.SecurityProtocol = SecurityProtocol.SaslPlaintext;
    config.SaslMechanism = SaslMechanism.Plain;
    config.SaslUsername = username;
    config.SaslPassword = password;
    return config;
}

static async Task EnsureTopicAsync(IAdminClient admin, string topic)
{
    try
    {
        await admin.CreateTopicsAsync([new TopicSpecification { Name = topic, NumPartitions = 1, ReplicationFactor = 1 }]);
    }
    catch (CreateTopicsException exception) when (exception.Results.All(static result => result.Error.Code is ErrorCode.TopicAlreadyExists))
    {
        Console.WriteLine("topic-already-exists=" + topic);
    }
}

internal sealed record CapturedActivity(
    string TraceId,
    string SpanId,
    string Name,
    string Kind,
    string Status,
    string StatusDescription,
    IReadOnlyDictionary<string, string> Tags,
    CapturedEvent[] Events)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.TraceId.ToHexString(),
            activity.SpanId.ToHexString(),
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.StatusDescription ?? string.Empty,
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal),
            [.. activity.Events.Select(static activityEvent => new CapturedEvent(
                activityEvent.Name,
                activityEvent.Tags
                    .Where(static tag => tag.Key is "exception.type")
                    .ToDictionary(
                        static tag => tag.Key,
                        static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                        StringComparer.Ordinal)))]);
}

internal sealed record CapturedEvent(string Name, IReadOnlyDictionary<string, string> Tags);

internal sealed record KafkaReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    // The longest status description Spans.RecordFailure writes.
    private const int MaxStatusDescriptionLength = 256;

    public static KafkaReport Create(string runtimeMode, CapturedActivity[] activities, string expectedFailureMessage)
    {
        var failures = new List<string>();
        var kafkaSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "messaging.kafka"))
            .ToArray();

        var sendSuccess = kafkaSpans
            .Where(static span => HasTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationType, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationTypeValues.Send) &&
                                  StringComparer.Ordinal.Equals(span.Status, "Unset"))
            .ToArray();
        var sendError = kafkaSpans
            .Where(static span => HasTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationType, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationTypeValues.Send) &&
                                  StringComparer.Ordinal.Equals(span.Status, "Error"))
            .ToArray();
        var receive = kafkaSpans
            .Where(static span => HasTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationType, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationTypeValues.Receive))
            .ToArray();

        if (sendSuccess.Length != 2)
            failures.Add($"expected 2 successful Kafka producer spans, got {sendSuccess.Length}");

        if (sendError.Length != 1)
            failures.Add($"expected 1 error Kafka producer span, got {sendError.Length}");

        if (receive.Length < 2)
            failures.Add($"expected at least 2 Kafka consumer spans, got {receive.Length}");

        foreach (var span in sendSuccess)
        {
            RequireTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.System, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.SystemValues.Kafka, failures);
            RequireTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationName, "send", failures);
            RequireTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.DestinationName, "qyl-probe", failures);
            RequireKind(span, "Producer", failures);
        }

        foreach (var span in sendError)
        {
            // The failure triple of Spans.RecordFailure, which the interceptor records: status Error
            // with the exception's message as the description (the one message carrier that survives
            // ingest), error.type bounded to the generic type's name, and the exception event.
            RequireTag(span, Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, "Confluent.Kafka.ProduceException`2", failures);
            RequireKind(span, "Producer", failures);
            var description = expectedFailureMessage.Length <= MaxStatusDescriptionLength
                ? expectedFailureMessage
                : expectedFailureMessage[..MaxStatusDescriptionLength];
            if (description.Length is 0 || !StringComparer.Ordinal.Equals(span.StatusDescription, description))
                failures.Add($"expected the failed producer span's status description '{description}', got '{span.StatusDescription}'");
            var exceptionEvents = span.Events.Where(static activityEvent => activityEvent.Name is "exception").ToArray();
            if (exceptionEvents.Length != 1 ||
                !exceptionEvents[0].Tags.TryGetValue("exception.type", out var exceptionType) ||
                !exceptionType.StartsWith("Confluent.Kafka.ProduceException`2", StringComparison.Ordinal))
            {
                failures.Add($"expected one exception event of Confluent.Kafka.ProduceException`2 on the failed producer span, got [{string.Join(", ", span.Events.Select(static activityEvent => activityEvent.Name))}]");
            }
        }

        foreach (var span in receive)
        {
            RequireTag(span, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.System, Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.SystemValues.Kafka, failures);
            RequireKind(span, "Client", failures);
        }

        foreach (var span in kafkaSpans)
        {
            span.Tags.TryGetValue(Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.OperationName, out var operationName);
            // Producers name the span "{operation} {destination}"; the pull-based Consume() span has no
            // destination and is named after the operation alone.
            var expectedName = span.Tags.TryGetValue(Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes.DestinationName, out var destination) && destination.Length > 0
                ? operationName + " " + destination
                : operationName;
            if (!StringComparer.Ordinal.Equals(span.Name, expectedName))
                failures.Add($"expected the Kafka span to be named '{expectedName}', got {span.Name}");
        }

        return new KafkaReport(runtimeMode, failures.Count is 0, failures.ToArray(), kafkaSpans);
    }

    private static bool HasTag(CapturedActivity span, string key, string expected)
        => span.Tags.TryGetValue(key, out var actual) && StringComparer.Ordinal.Equals(actual, expected);

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

    private static void RequireKind(CapturedActivity span, string expected, ICollection<string> failures)
    {
        if (!StringComparer.Ordinal.Equals(span.Kind, expected))
            failures.Add($"expected kind {expected}, got {span.Kind}");
    }
}

[JsonSerializable(typeof(KafkaReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealKafkaJsonContext : JsonSerializerContext;
