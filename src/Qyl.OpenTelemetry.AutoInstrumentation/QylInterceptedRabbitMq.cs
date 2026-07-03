using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Rabbit Mq.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylInterceptedRabbitMq
{

    /// <summary>Runs the Start Publish Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartPublishActivity(string? exchange)
        => QylMessagingActivityPolicy.StartRabbitMqPublishActivity(exchange);

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
