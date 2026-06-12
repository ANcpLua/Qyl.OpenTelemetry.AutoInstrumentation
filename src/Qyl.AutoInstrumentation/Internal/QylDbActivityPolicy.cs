using System.Data;
using System.Data.Common;
using System.Diagnostics;

namespace Qyl.AutoInstrumentation.Internal;

internal static class QylDbActivityPolicy
{
    public static Activity? StartDbCommandActivity(DbCommand command, string instrumentationId, string operationName)
    {
        var operation = NormalizeOperation(operationName, command);
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            QylActivityNames.DbCommand(operation),
            ActivityKind.Client,
            QylInstrumentationDomains.DbClient);
        if (activity is null)
            return null;

        QylActivityTags.SetDb(
            activity,
            GetDbSystemName(instrumentationId),
            operation,
            GetQuerySummary(command, operation));

        var databaseName = command.Connection?.Database;
        if (!string.IsNullOrWhiteSpace(databaseName))
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);

        QylSensitiveCapturePolicy.SetDbQueryText(activity, command, instrumentationId);
        return activity;
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
