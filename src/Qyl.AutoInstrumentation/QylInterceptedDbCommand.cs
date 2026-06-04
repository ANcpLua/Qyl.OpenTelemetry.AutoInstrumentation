using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Qyl.AutoInstrumentation;

public static class QylInterceptedDbCommand
{
    private const string DbDomain = "db.client";

    public static Activity? StartActivity(DbCommand command, string instrumentationId, string operationName)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(instrumentationId);
        ArgumentNullException.ThrowIfNull(operationName);

        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, instrumentationId))
            return null;

        var operation = NormalizeOperation(operationName, command);
        var activity = QylActivitySource.Source.StartActivity("DB " + operation, ActivityKind.Client);
        if (activity is null)
            return null;

        activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, DbDomain);
        activity.SetTag(QylSemanticAttributes.DbSystemName, GetDbSystemName(instrumentationId));
        activity.SetTag(QylSemanticAttributes.DbOperationName, operation);
        activity.SetTag(QylSemanticAttributes.DbQuerySummary, GetQuerySummary(command, operation));

        var databaseName = command.Connection?.Database;
        if (!string.IsNullOrWhiteSpace(databaseName))
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);

        if (ShouldCaptureCommandText(command, instrumentationId))
            activity.SetTag(QylSemanticAttributes.DbQueryText, command.CommandText);

        return activity;
    }

    public static void RecordSuccess(Activity? activity)
    {
    }

    public static async Task<T> ObserveAsync<T>(Task<T> task, Activity? activity, long metricStart, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(task);
        ArgumentNullException.ThrowIfNull(instrumentationId);

        try
        {
            var result = await task.ConfigureAwait(false);
            RecordSuccess(activity);
            QylDbClientMetrics.RecordDuration(metricStart, instrumentationId);
            return result;
        }
        catch (Exception exception)
        {
            RecordException(activity, exception);
            QylDbClientMetrics.RecordDuration(metricStart, instrumentationId);
            throw;
        }
        finally
        {
            activity?.Dispose();
        }
    }

    public static void RecordException(Activity? activity, Exception exception)
    {
        activity?.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);
        activity?.SetStatus(ActivityStatusCode.Error);
    }

    private static bool ShouldCaptureCommandText(DbCommand command, string instrumentationId)
    {
        if (string.IsNullOrWhiteSpace(command.CommandText))
            return false;

        if (command.CommandType is CommandType.StoredProcedure)
            return true;

        var options = QylAutoInstrumentationOptions.Current;
        return instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => options.SqlClientSetDbStatementForText,
            QylAutoInstrumentationIds.EntityFrameworkCore => options.EntityFrameworkCoreSetDbStatementForText,
            QylAutoInstrumentationIds.OracleMda => options.OracleMdaSetDbStatementForText,
            _ => false,
        };
    }

    private static string GetQuerySummary(DbCommand command, string operation)
    {
        if (command.CommandType is CommandType.StoredProcedure && !string.IsNullOrWhiteSpace(command.CommandText))
            return command.CommandText;

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
