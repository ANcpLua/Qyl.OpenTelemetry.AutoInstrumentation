using System.Diagnostics;
using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylMessagingActivityPolicy
{
    // messaging.operation.name is the system-specific verb and carries no registry value set;
    // messaging.operation.type's own "publish" member is deprecated in favour of "send", so every
    // qyl producer span reports OperationTypeValues.Send and distinguishes itself by NAME here.
    private const string Send = "send";
    private const string Receive = "receive";

    public static Activity? StartKafkaProducerActivity(string? topic, int? partitionId)
    {
        var destination = string.IsNullOrEmpty(topic) ? null : topic;
        var activity = Start(
            QylAutoInstrumentationIds.Kafka,
            SpanNames.MessagingSendProducer(Send, destinationTemplate: null, destination, serverAddress: null, serverPort: null),
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            MessagingAttributes.SystemValues.Kafka,
            MessagingAttributes.OperationTypeValues.Send,
            Send,
            destination);
        if (activity is not null && partitionId is int id)
            activity.SetTag(MessagingAttributes.DestinationPartitionId, id.ToString(CultureInfo.InvariantCulture));

        return activity;
    }

    public static Activity? StartKafkaConsumerActivity()
        => Start(
            QylAutoInstrumentationIds.Kafka,
            SpanNames.MessagingReceiveClient(Receive, destinationTemplate: null, destinationName: null, serverAddress: null, serverPort: null),
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            MessagingAttributes.SystemValues.Kafka,
            MessagingAttributes.OperationTypeValues.Receive,
            Receive,
            destination: null);

    private static Activity? Start(
        string instrumentationId,
        string name,
        ActivityKind kind,
        string domain,
        string system,
        string operationType,
        string operationName,
        string? destination)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            name,
            kind,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetMessaging(activity, system, operationType, operationName, destination);
        return activity;
    }
}
