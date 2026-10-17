using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>The activity observation shared by every generated interceptor body.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedActivity
{
    /// <summary>Records the failure of the intercepted call on the activity with
    /// <c>Spans.RecordFailure</c>: status Error with the message as the description, <c>error.type</c>
    /// and the exception event.</summary>
    public static void RecordFailure(Activity? activity, Exception exception)
    {
        if (activity is not null)
            QylActivityStatus.RecordFailure(activity, exception);
    }

    /// <summary>Observes an asynchronous operation and records its failure on the activity.</summary>
    [JetBrains.Annotations.UsedImplicitly]
    public static Task ObserveAsync(Task? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Observes an asynchronous operation and records its failure on the activity.</summary>
    [JetBrains.Annotations.UsedImplicitly]
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);
}
