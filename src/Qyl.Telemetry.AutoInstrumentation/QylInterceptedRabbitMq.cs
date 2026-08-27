using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>RabbitMQ.Client publish spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.RabbitMq, QylInstrumentationDomains.MessagingRabbitMq)]
[QylIntercept("RabbitMQ.Client.IModel", "BasicPublish", "BasicPublishAsync", Shape = QylShapes.RabbitMqPublish, Start = nameof(Publish))]
[QylIntercept("RabbitMQ.Client.IChannel", "BasicPublish", "BasicPublishAsync", Shape = QylShapes.RabbitMqPublish, Start = nameof(Publish))]
public static class QylInterceptedRabbitMq
{
    /// <summary>Starts the publish span for an exchange and routing key.</summary>
    public static Activity? Publish(
        [QylFromArgument(0, Type = "string")]
        [QylFromArgument(0, Type = "RabbitMQ.Client.PublicationAddress", Convert = "{0}.ExchangeName")]
        string? exchange,
        [QylFromArgument(1, Type = "string")]
        [QylFromArgument(0, Type = "RabbitMQ.Client.PublicationAddress", Convert = "{0}.RoutingKey")]
        string? routingKey)
        => QylMessagingActivityPolicy.StartRabbitMqPublishActivity(exchange, routingKey);
}
