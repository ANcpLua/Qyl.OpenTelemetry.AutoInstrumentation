using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Mass Transit.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedMassTransit);</code></example>
public static class QylInterceptedMassTransit
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(string operationName)
    {
        var operation = string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;

        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.MassTransit,
            QylActivityNames.MassTransitMessage,
            ActivityKind.Producer,
            QylInstrumentationDomains.MessagingMassTransit);
        if (activity is null)
            return null;

        QylActivityTags.SetMessaging(
            activity,
            QylSemanticAttributes.MessagingSystemMassTransit,
            QylSemanticAttributes.MessagingOperationTypeSend,
            operation);
        return activity;
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
