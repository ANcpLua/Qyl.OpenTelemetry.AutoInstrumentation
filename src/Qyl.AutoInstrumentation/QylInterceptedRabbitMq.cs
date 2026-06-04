using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedRabbitMq
{
    private const string RabbitMqDomain = "messaging.rabbitmq";

    public static Activity? StartPublishActivity(string? exchange)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.RabbitMq))
            return null;

        var activity = QylActivitySource.Source.StartActivity("RabbitMQ publish", ActivityKind.Producer);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, RabbitMqDomain);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemRabbitMq);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, QylSemanticAttributes.MessagingOperationNamePublish);

        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }
}
