using System.Data.Common;
using System.Diagnostics;
using Qyl.AutoInstrumentation.Internal;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Intercepted database Command.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylInterceptedDbCommand);</code></example>
public static class QylInterceptedDbCommand
{

    /// <summary>Runs the Start Activity runtime helper used by source-generated qyl interceptors.</summary>
    public static Activity? StartActivity(DbCommand command, string instrumentationId, string operationName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(instrumentationId);
        ArgumentNullException.ThrowIfNull(operationName);

        return QylDbActivityPolicy.StartDbCommandActivity(command, instrumentationId, operationName);
    }

    /// <summary>Runs the Get Timestamp runtime helper used by source-generated qyl interceptors.</summary>
    public static long GetTimestamp()
        => QylDurationMetrics.GetDbClientStartTimestamp();

    /// <summary>Runs the Record Duration runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordDuration(long startTimestamp, string instrumentationId)
        => QylDurationMetrics.RecordDbClientDuration(startTimestamp, instrumentationId);

    /// <summary>Observes an asynchronous database command and records qyl success, exception, and duration telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T> task, Activity? activity, long metricStart, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(instrumentationId);

        if (activity is null && !QylDurationMetrics.IsDbClientRecordingEnabled(instrumentationId))
            return task;

        return ObserveSlowAsync(task, activity, metricStart, instrumentationId);
    }

    private static async Task<T> ObserveSlowAsync<T>(Task<T> task, Activity? activity, long metricStart, string instrumentationId)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            QylDurationMetrics.RecordDbClientDuration(metricStart, instrumentationId);
            return result;
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            QylDurationMetrics.RecordDbClientDuration(metricStart, instrumentationId);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    /// <summary>Runs the Record Exception runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordException(Activity? activity, Exception exception)
    {
        QylActivityStatus.RecordException(activity, exception);
    }


}
