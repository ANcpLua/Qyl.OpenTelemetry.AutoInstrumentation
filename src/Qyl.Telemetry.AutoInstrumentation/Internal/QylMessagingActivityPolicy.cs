using System.Diagnostics;
using System.Globalization;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylMessagingActivityPolicy
{
    private const string Send = "send";
    private const string Publish = "publish";
    private const string Receive = "receive";
    private const string RabbitMqDefaultDestination = "amq.default";

    public static Activity? StartKafkaProducerActivity(string? topic, int? partitionId)
    {
        var activity = Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeSend,
            Send,
            destination: string.IsNullOrEmpty(topic) ? null : topic);
        if (activity is not null && partitionId is int id)
            activity.SetTag(QylSemanticAttributes.MessagingDestinationPartitionId, id.ToString(CultureInfo.InvariantCulture));

        return activity;
    }

    public static Activity? StartKafkaConsumerActivity()
        => Start(
            QylAutoInstrumentationIds.Kafka,
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.MessagingKafka,
            QylSemanticAttributes.MessagingSystemKafka,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            Receive,
            destination: null);

    public static Activity? StartMassTransitActivity(string method)
        => Start(
            QylAutoInstrumentationIds.MassTransit,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingMassTransit,
            QylSemanticAttributes.MessagingSystemMassTransit,
            QylSemanticAttributes.MessagingOperationTypeSend,
            OperationName(method),
            destination: null);

    public static Activity? StartNServiceBusActivity(string method)
        => Start(
            QylAutoInstrumentationIds.NServiceBus,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingNServiceBus,
            QylSemanticAttributes.MessagingSystemNServiceBus,
            QylSemanticAttributes.MessagingOperationTypeSend,
            OperationName(method),
            destination: null);

    public static Activity? StartRabbitMqPublishActivity(string? exchange, string? routingKey)
    {
        var activity = Start(
            QylAutoInstrumentationIds.RabbitMq,
            ActivityKind.Producer,
            QylAttributes.InstrumentationDomainValues.MessagingRabbitMq,
            QylSemanticAttributes.MessagingSystemRabbitMq,
            QylSemanticAttributes.MessagingOperationTypeSend,
            Publish,
            RabbitMqDestination(exchange, routingKey));
        if (activity is not null && !string.IsNullOrEmpty(routingKey))
            activity.SetTag(QylSemanticAttributes.MessagingRabbitMqRoutingKey, routingKey);

        return activity;
    }

    // RabbitMQ destination convention: {exchange}:{routing_key}; the available one alone when the
    // other is empty; amq.default only when both are empty.
    private static string RabbitMqDestination(string? exchange, string? routingKey)
    {
        var hasExchange = !string.IsNullOrEmpty(exchange);
        var hasRoutingKey = !string.IsNullOrEmpty(routingKey);
        if (!hasExchange && !hasRoutingKey)
            return RabbitMqDefaultDestination;
        if (!hasExchange)
            return routingKey!;
        if (!hasRoutingKey)
            return exchange!;

        return exchange + ":" + routingKey;
    }

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
