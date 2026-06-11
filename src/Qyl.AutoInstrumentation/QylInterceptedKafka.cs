using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Kafka.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedKafka);</code></example>
public static class QylInterceptedKafka
{
    /// <summary>Runs the Start Producer Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartProducerActivity()
        => StartActivity(
            ActivityKind.Producer,
            QylSemanticAttributes.MessagingOperationTypeSend,
            QylSemanticAttributes.MessagingOperationNamePublish);

    /// <summary>Runs the Start Consumer Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartConsumerActivity()
        => StartActivity(
            ActivityKind.Consumer,
            QylSemanticAttributes.MessagingOperationTypeReceive,
            QylSemanticAttributes.MessagingOperationTypeReceive);

    /// <summary>Runs the Record Consume Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordConsumeSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static Activity? StartActivity(ActivityKind activityKind, string operationType, string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.Kafka))
            return null;

        if (QylActivitySource.StartActivity(QylActivityNames.KafkaMessage, activityKind) is not { } activity)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.MessagingKafka);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemKafka);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, operationType);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, operationName);

        return activity;
    }
}
