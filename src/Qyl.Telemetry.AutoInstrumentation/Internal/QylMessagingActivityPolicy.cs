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
    private const string Publish = "publish";
    private const string Receive = "receive";
    // messaging.system does not enumerate NServiceBus, so the value is qyl-owned.
    internal const string NServiceBusSystemName = "nservicebus";

    public static Activity? StartKafkaProducerActivity(string? topic, int? partitionId)
    {
        var activity = Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            MessagingAttributes.SystemValues.Kafka,
            MessagingAttributes.OperationTypeValues.Send,
            Send,
            destination: string.IsNullOrEmpty(topic) ? null : topic);
        if (activity is not null && partitionId is int id)
            activity.SetTag(MessagingAttributes.DestinationPartitionId, id.ToString(CultureInfo.InvariantCulture));

        return activity;
    }

    public static Activity? StartKafkaConsumerActivity()
        => Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            MessagingAttributes.SystemValues.Kafka,
            MessagingAttributes.OperationTypeValues.Receive,
            Receive,
            destination: null);

    public static Activity? StartNServiceBusActivity(string method)
        => Start(
            QylAutoInstrumentationIds.NServiceBus,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingNServiceBus,
            NServiceBusSystemName,
            MessagingAttributes.OperationTypeValues.Send,
            OperationName(method),
            destination: null);

    public static string OperationName(string method)
        => string.Equals(method, "Send", StringComparison.Ordinal) ? Send : Publish;

    private static Activity? Start(
        string instrumentationId,
        ActivityKind kind,
        string domain,
        string system,
        string operationType,
        string operationName,
        string? destination)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            QylSpanNames.Messaging(operationName, destination),
            kind,
            domain);
        if (activity is null)
            return null;

        QylActivityTags.SetMessaging(activity, system, operationType, operationName, destination);
        return activity;
    }
}
