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

        var operation = DatabaseSemantics.NormalizeOperation(null, commandEvent.Command.CommandText);
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
            "Microsoft.EntityFrameworkCore.Sqlite" => "sqlite",
            "Microsoft.EntityFrameworkCore.SqlServer" => "microsoft.sql_server",
            "Npgsql.EntityFrameworkCore.PostgreSQL" => "postgresql",
            "Pomelo.EntityFrameworkCore.MySql" => "mysql",
            "MySql.EntityFrameworkCore" => "mysql",
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
