using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedKafka
{
    private const string KafkaDomain = "messaging.kafka";

    public static Activity? StartProducerActivity(string? topic)
        => StartActivity(
            ActivityKind.Producer,
            QylSemanticAttributes.MessagingOperationTypeSend,
            QylSemanticAttributes.MessagingOperationNamePublish,
            topic);

    public static Activity? StartConsumerActivity()
        => StartActivity(
            ActivityKind.Consumer,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            null);

    public static void RecordConsumeSuccess(Activity? activity, string? topic)
    {
        if (activity is not null && !string.IsNullOrWhiteSpace(topic))
            activity.SetTag(QylSemanticAttributes.MessagingDestinationName, topic);
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static Activity? StartActivity(ActivityKind activityKind, string operationType, string operationName, string? topic)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Kafka))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Kafka " + operationName, activityKind);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, KafkaDomain);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemKafka);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, operationType);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operationName);

        if (!string.IsNullOrWhiteSpace(topic))
            activity.SetTag(QylSemanticAttributes.MessagingDestinationName, topic);

        return activity;
    }
}
