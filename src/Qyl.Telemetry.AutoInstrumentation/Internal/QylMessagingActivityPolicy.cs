using System.Diagnostics;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylMessagingActivityPolicy
{
    private const string Send = "send";
    private const string Publish = "publish";
    private const string Receive = "receive";
    private const string RabbitMqDefaultExchange = "amq.default";

    public static Activity? StartKafkaProducerActivity()
        => Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeSend,
            Send,
            destination: null);

    public static Activity? StartKafkaConsumerActivity()
        => Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Client,
            QylInstrumentationDomains.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            Receive,
            destination: null);

    public static Activity? StartMassTransitActivity(string method)
        => Start(
            QylAutoInstrumentationIds.MassTransit,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingMassTransit,
            QylSemanticAttributes.MessagingSystemMassTransit,
            QylSemanticAttributes.MessagingOperationTypeSend,
            OperationName(method),
            destination: null);

    public static Activity? StartNServiceBusActivity(string method)
        => Start(
            QylAutoInstrumentationIds.NServiceBus,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingNServiceBus,
            QylSemanticAttributes.MessagingSystemNServiceBus,
            QylSemanticAttributes.MessagingOperationTypeSend,
            OperationName(method),
            destination: null);

    public static Activity? StartRabbitMqPublishActivity(string? exchange)
        => Start(
            QylAutoInstrumentationIds.RabbitMq,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingRabbitMq,
            QylSemanticAttributes.MessagingSystemRabbitMq,
            QylSemanticAttributes.MessagingOperationTypeSend,
            Publish,
            string.IsNullOrEmpty(exchange) ? RabbitMqDefaultExchange : exchange);

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
