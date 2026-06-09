using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Rabbit Mq.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedRabbitMq);</code></example>
public static class QylInterceptedRabbitMq
{

    /// <summary>Runs the Start Publish Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartPublishActivity(string? exchange)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylAutoInstrumentationIds.RabbitMq))
            return null;

        var activity = QylActivitySource.StartActivity("RabbitMQ publish", ActivityKind.Producer);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.MessagingRabbitMq);
        activity.SetTag(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemRabbitMq);
        activity.SetTag(QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend);
        activity.SetTag(QylSemanticAttributes.MessagingOperationName, QylSemanticAttributes.MessagingOperationNamePublish);

        return activity;
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
}
