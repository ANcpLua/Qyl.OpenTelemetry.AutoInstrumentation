using System.Diagnostics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Internal;

internal static class QylMessagingActivityPolicy
{
    public static Activity? StartKafkaProducerActivity()
        => StartMessagingActivity(
            QylAutoInstrumentationIds.Kafka,
            QylActivityNames.KafkaMessage,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeSend,
            QylSemanticAttributes.MessagingOperationNamePublish);

    public static Activity? StartKafkaConsumerActivity()
        => StartMessagingActivity(
            QylAutoInstrumentationIds.Kafka,
            QylActivityNames.KafkaMessage,
            ActivityKind.Consumer,
            QylInstrumentationDomains.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            QylSemanticAttributes.MessagingOperationTypeReceive);

    public static Activity? StartMassTransitActivity(string operationName)
        => StartMessagingActivity(
            QylAutoInstrumentationIds.MassTransit,
            QylActivityNames.MassTransitMessage,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingMassTransit,
            QylSemanticAttributes.MessagingSystemMassTransit,
            QylSemanticAttributes.MessagingOperationTypeSend,
            NormalizeSendPublishOperation(operationName));

    public static Activity? StartNServiceBusActivity(string operationName)
        => StartMessagingActivity(
            QylAutoInstrumentationIds.NServiceBus,
            QylActivityNames.NServiceBusMessage,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingNServiceBus,
            QylSemanticAttributes.MessagingSystemNServiceBus,
            QylSemanticAttributes.MessagingOperationTypeSend,
            NormalizeSendPublishOperation(operationName));

    public static Activity? StartRabbitMqPublishActivity(string? exchange)
        => StartMessagingActivity(
            QylAutoInstrumentationIds.RabbitMq,
            QylActivityNames.RabbitMqPublish,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingRabbitMq,
            QylSemanticAttributes.MessagingSystemRabbitMq,
            QylSemanticAttributes.MessagingOperationTypeSend,
            QylSemanticAttributes.MessagingOperationNamePublish);

    private static Activity? StartMessagingActivity(
        string instrumentationId,
        string activityName,
        ActivityKind activityKind,
        string instrumentationDomain,
        string messagingSystem,
        string operationType,
        string operationName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            activityName,
            activityKind,
            instrumentationDomain);
        if (activity is null)
            return null;

        QylActivityTags.SetMessaging(activity, messagingSystem, operationType, operationName);
        return activity;
    }

    private static string NormalizeSendPublishOperation(string operationName)
        => string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;
}
