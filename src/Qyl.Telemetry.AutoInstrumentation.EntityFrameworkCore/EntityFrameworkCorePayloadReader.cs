using Microsoft.EntityFrameworkCore.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore;

internal static class EntityFrameworkCorePayloadReader
{
    public static bool TryRead(object? payload, out EntityFrameworkCoreCommand command)
    {
        if (payload is not CommandEventData commandEvent)
        {
            command = default;
            return false;
        }

        var (operation, summary) = QylDbQuerySummary.Describe(commandEvent.Command.CommandType, commandEvent.Command.CommandText);
        command = new EntityFrameworkCoreCommand(
            DbSystem: MapProviderName(commandEvent.Context?.Database.ProviderName),
            Namespace: NormalizeEmpty(commandEvent.Command.Connection?.Database),
            Operation: operation,
            QuerySummary: summary,
            QueryText: commandEvent.Command.CommandText,
            ErrorType: payload is CommandErrorEventData errorEvent
                ? errorEvent.Exception.GetType().FullName
                : null,
            StartTime: commandEvent is CommandEndEventData endEvent ? endEvent.StartTime : TimeProvider.System.GetUtcNow(),
            Duration: commandEvent is CommandEndEventData timedEvent ? timedEvent.Duration : TimeSpan.Zero);

        return true;
    }

    // Every provider that raises relational command events speaks SQL; an unmapped one is other_sql.
    private static string MapProviderName(string? providerName)
        => providerName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => QylSemanticAttributes.DbSystemSqlite,
            "Microsoft.EntityFrameworkCore.SqlServer" => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => QylSemanticAttributes.DbSystemPostgresql,
            "Pomelo.EntityFrameworkCore.MySql" => QylSemanticAttributes.DbSystemMysql,
            "MySql.EntityFrameworkCore" => QylSemanticAttributes.DbSystemMysql,
            "Oracle.EntityFrameworkCore" => QylSemanticAttributes.DbSystemOracleDb,
            "IBM.EntityFrameworkCore" => QylSemanticAttributes.DbSystemIbmDb2,
            _ => QylSemanticAttributes.DbSystemOtherSql,
        };

    private static string? NormalizeEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal readonly record struct EntityFrameworkCoreCommand(
    string DbSystem,
    string? Namespace,
    string? Operation,
    string? QuerySummary,
    string? QueryText,
    string? ErrorType,
    DateTimeOffset StartTime,
    TimeSpan Duration);
