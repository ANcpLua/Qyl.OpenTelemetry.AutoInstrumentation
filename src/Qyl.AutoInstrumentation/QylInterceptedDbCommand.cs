using System.Data;
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

        var options = QylAutoInstrumentationOptions.Current;
        if (!options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, instrumentationId))
            return null;

        var operation = NormalizeOperation(operationName, command);
        var activity = QylActivitySource.StartActivity(QylActivityNames.DbCommand(operation), ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbClient);
        activity.SetTag(QylSemanticAttributes.DbSystemName, GetDbSystemName(instrumentationId));
        activity.SetTag(QylSemanticAttributes.DbOperationName, operation);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, GetQuerySummary(command, operation));

        var databaseName = command.Connection?.Database;
        if (!string.IsNullOrWhiteSpace(databaseName))
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);

        QylSensitiveCapturePolicy.SetDbQueryText(activity, command, instrumentationId);

        return activity;
    }

    /// <summary>Runs the Record Success runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordSuccess(Activity? activity)
    {
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
            RecordSuccess(activity);
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

    private static string GetQuerySummary(DbCommand command, string operation)
    {
        return operation;
    }

    private static string NormalizeOperation(string operationName, DbCommand command)
    {
        if (command.CommandType is CommandType.StoredProcedure)
            return "CALL";

        var text = command.CommandText;
        if (!string.IsNullOrWhiteSpace(text))
        {
            var token = FirstToken(text);
            if (IsKnownDbOperation(token))
                return token;
        }

        return operationName switch
        {
            "ExecuteNonQuery" or "ExecuteNonQueryAsync" => "EXECUTE",
            "ExecuteScalar" or "ExecuteScalarAsync" => "EXECUTE",
            "ExecuteReader" or "ExecuteReaderAsync" => "SELECT",
            _ => "EXECUTE",
        };
    }

    private static string FirstToken(string text)
    {
        var index = 0;
        while (index < text.Length && char.IsWhiteSpace(text[index]))
            index++;

        var start = index;
        while (index < text.Length && char.IsLetter(text[index]))
            index++;

        return index == start ? string.Empty : text[start..index].ToUpperInvariant();
    }

    private static bool IsKnownDbOperation(string token)
        => token is "SELECT" or "INSERT" or "UPDATE" or "DELETE" or "MERGE" or "CALL" or "CREATE" or "ALTER" or "DROP" or "TRUNCATE" or "EXEC" or "EXECUTE";

    private static string GetDbSystemName(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            QylAutoInstrumentationIds.Sqlite => QylSemanticAttributes.DbSystemSqlite,
            QylAutoInstrumentationIds.Npgsql => QylSemanticAttributes.DbSystemPostgresql,
            QylAutoInstrumentationIds.MySqlConnector => QylSemanticAttributes.DbSystemMysql,
            QylAutoInstrumentationIds.MySqlData => QylSemanticAttributes.DbSystemMysql,
            QylAutoInstrumentationIds.OracleMda => QylSemanticAttributes.DbSystemOracleDb,
            _ => QylSemanticAttributes.DbSystemOtherSql,
        };
}
