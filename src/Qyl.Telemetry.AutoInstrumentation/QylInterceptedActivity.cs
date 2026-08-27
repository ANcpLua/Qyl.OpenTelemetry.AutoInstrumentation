using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>The activity observation shared by every generated interceptor body.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
public static class QylInterceptedActivity
{
    /// <summary>Records the exception the intercepted call threw on the activity.</summary>
    public static void RecordException(Activity? activity, Exception exception)
        => QylActivityStatus.RecordException(activity, exception);

    /// <summary>Observes an asynchronous operation and records qyl exception telemetry.</summary>
    public static Task ObserveAsync(Task? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);

    /// <summary>Observes an asynchronous operation and records qyl exception telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)
        => QylActivityObserver.ObserveAsync(task, activity);
}
