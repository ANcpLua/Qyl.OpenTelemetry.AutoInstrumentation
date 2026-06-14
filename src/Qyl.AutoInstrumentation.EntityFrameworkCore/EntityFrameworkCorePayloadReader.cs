using System.Data;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.EntityFrameworkCore;

internal static class EntityFrameworkCorePayloadReader
{
    public static bool TryRead(object? payload, out EntityFrameworkCoreCommand command)
    {
        if (payload is not CommandEventData commandEvent)
        {
            command = default;
            return false;
        }

        var operation = commandEvent.Command.CommandType is CommandType.StoredProcedure
            ? "CALL"
            : DatabaseSemantics.NormalizeOperation(null, commandEvent.Command.CommandText);
        command = new EntityFrameworkCoreCommand(
            DbSystem: MapProviderName(commandEvent.Context?.Database.ProviderName),
            Namespace: NormalizeEmpty(commandEvent.Command.Connection?.Database),
            Operation: operation,
            QuerySummary: DatabaseSemantics.CreateSummary(operation, commandEvent.CommandSource.ToString()),
            QueryText: commandEvent.Command.CommandText,
            ErrorType: payload is CommandErrorEventData errorEvent
                ? errorEvent.Exception.GetType().FullName
                : null);

        return true;
    }

    private static string? MapProviderName(string? providerName)
        => providerName switch
        {
            "Microsoft.EntityFrameworkCore.Sqlite" => QylSemanticAttributes.DbSystemSqlite,
            "Microsoft.EntityFrameworkCore.SqlServer" => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            "Npgsql.EntityFrameworkCore.PostgreSQL" => QylSemanticAttributes.DbSystemPostgresql,
            "Pomelo.EntityFrameworkCore.MySql" => QylSemanticAttributes.DbSystemMysql,
            "MySql.EntityFrameworkCore" => QylSemanticAttributes.DbSystemMysql,
            "Oracle.EntityFrameworkCore" => "oracle",
            "IBM.EntityFrameworkCore" => "db2",
            _ => null,
        };

    private static string? NormalizeEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value;
}

internal readonly record struct EntityFrameworkCoreCommand(
    string? DbSystem,
    string? Namespace,
    string? Operation,
    string? QuerySummary,
    string? QueryText,
    string? ErrorType);
