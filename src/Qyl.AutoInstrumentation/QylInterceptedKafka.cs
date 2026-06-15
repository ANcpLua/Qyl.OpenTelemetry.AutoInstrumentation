using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Kafka.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedKafka);</code></example>
public static class QylInterceptedKafka
{
    /// <summary>Runs the Start Producer Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartProducerActivity()
        => QylMessagingActivityPolicy.StartKafkaProducerActivity();

    /// <summary>Runs the Start Consumer Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartConsumerActivity()
        => QylMessagingActivityPolicy.StartKafkaConsumerActivity();

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }


}
