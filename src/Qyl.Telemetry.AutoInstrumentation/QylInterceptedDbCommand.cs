using System.Data.Common;
using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>
/// ADO.NET <see cref="DbCommand"/> execution spans, fanned out to the provider's instrumentation id
/// by the receiver's namespace. Providers that declare a native <c>ActivitySource</c> are excluded:
/// their spans are their own.
/// </summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.AdoNet, QylAttributes.InstrumentationDomainValues.DbClient)]
[QylIntercept(
    "System.Data.Common.DbCommand",
    "ExecuteReader", "ExecuteReaderAsync", "ExecuteScalar", "ExecuteScalarAsync", "ExecuteNonQuery", "ExecuteNonQueryAsync",
    Shape = QylShapes.DbCommand,
    Body = QylInterceptorBody.DbCommand,
    Start = nameof(Execute),
    Metric = nameof(RecordDuration),
    // Npgsql, MySqlConnector, MySql.Data and ODP.NET each declare their own ActivitySource, which
    // Qyl.Telemetry.Hosting subscribes. A command on one of those receivers is theirs to report.
    NativeSourceReceivers = ["Npgsql", "MySqlConnector", "MySql.Data", "Oracle.ManagedDataAccess"])]
[QylSignal(QylAutoInstrumentationIds.SqlClient, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.SqlClient, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.Sqlite, QylAutoInstrumentationSignal.Traces)]
public static class QylInterceptedDbCommand
{
    /// <summary>Starts the client span for the command under the provider's instrumentation id.</summary>
    public static Activity? Execute(
        [QylFromReceiver] DbCommand command,
        [QylFromInstrumentationId] string instrumentationId,
        [QylFromMethodName] string operationName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(instrumentationId);
        ArgumentNullException.ThrowIfNull(operationName);

        return QylDbActivityPolicy.StartDbCommandActivity(command, instrumentationId);
    }

    /// <summary>Reads the metric start timestamp, or zero when the metric is not recording.</summary>
    public static long GetTimestamp()
        => QylDbClientMetrics.GetTimestamp();

    /// <summary>Records the operation duration since <paramref name="startTimestamp"/>.</summary>
    public static void RecordDuration(long startTimestamp, [QylFromInstrumentationId] string instrumentationId)
        => QylDbClientMetrics.RecordDuration(startTimestamp, instrumentationId);

    /// <summary>Observes an asynchronous database command and records qyl success, exception, and duration telemetry.</summary>
    public static Task<T> ObserveAsync<T>(Task<T> task, Activity? activity, long metricStart, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(instrumentationId);

        if (activity is null && !QylDbClientMetrics.IsRecordingEnabled(instrumentationId))
            return task;

        return ObserveSlowAsync(task, activity, metricStart, instrumentationId);
    }

    private static async Task<T> ObserveSlowAsync<T>(Task<T> task, Activity? activity, long metricStart, string instrumentationId)
    {
        try
        {
            var result = await task.ConfigureAwait(false);
            QylDbClientMetrics.RecordDuration(metricStart, instrumentationId);
            return result;
        }
        catch (Exception exception)
        {
            QylInterceptedActivity.RecordException(activity, exception);
            QylDbClientMetrics.RecordDuration(metricStart, instrumentationId);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }
}
