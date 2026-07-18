using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted Quartz.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedQuartz
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity()
    {
        return QylActivityFactory.StartTraceActivity(
            QylAutoInstrumentationIds.Quartz,
            QylActivityNames.QuartzExecute,
            ActivityKind.Internal,
            QylInstrumentationDomains.JobQuartz);
    }

    /// <summary>Runs the Observe Async runtime helper used by source-generated qyl interceptors.</summary>
    public static Task ObserveAsync(Task? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }
}
