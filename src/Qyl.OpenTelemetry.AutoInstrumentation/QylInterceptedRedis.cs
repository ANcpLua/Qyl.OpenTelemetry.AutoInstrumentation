using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Redis.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedRedis
{

    /// <summary>Runs the Start Command Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartCommandActivity(string operationName)
    {
        var activity = QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.StackExchangeRedis,
            QylActivityNames.RedisCommand,
            ActivityKind.Client,
            QylInstrumentationDomains.DbRedis);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            QylSemanticAttributes.DbSystemRedis,
            operationName,
            operationName);

        return activity;
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
