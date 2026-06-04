using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedKafka
{
    private const string KafkaDomain = "messaging.kafka";

    public static Activity? StartProducerActivity(string? topic)
        => StartActivity("publish", topic);

    public static Activity? StartConsumerActivity()
        => StartActivity("receive", null);

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

    private static Activity? StartActivity(string operation, string? topic)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Kafka))
            return null;

        var activity = QylActivitySource.Source.StartActivity("Kafka " + operation, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, KafkaDomain);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, "kafka");
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operation);

        if (!string.IsNullOrWhiteSpace(topic))
            activity.SetTag(QylSemanticAttributes.MessagingDestinationName, topic);

        return activity;
    }
}
