using System.Data;
using System.Globalization;
using Microsoft.Data.SqlClient;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SqlClient;

internal static class SqlClientPayloadReader
{
    private const string CommandKey = "Command";
    private const string ExceptionKey = "Exception";

    public static bool TryRead(object? payload, bool isError, out SqlClientCommand command)
    {
        if (!TryGetPayloadValue<SqlCommand>(payload, CommandKey, out var sqlCommand) ||
            sqlCommand is null)
        {
            command = default;
            return false;
        }

        _ = TryGetPayloadValue<Exception>(payload, ExceptionKey, out var exception);

        var operation = NormalizeOperation(sqlCommand.CommandType, sqlCommand.CommandText);
        var endpoint = ParseDataSource(sqlCommand.Connection?.DataSource);

        command = new SqlClientCommand(
            Namespace: NormalizeEmpty(sqlCommand.Connection?.Database),
            Operation: operation,
            QuerySummary: DatabaseSemantics.CreateSummary(operation, sqlCommand.CommandType.ToString()),
            QueryText: sqlCommand.CommandText,
            ServerAddress: endpoint.Address,
            ServerPort: endpoint.Port,
            ErrorType: isError ? GetErrorType(exception) : null);

        return true;
    }

    private static bool TryGetPayloadValue<T>(object? payload, string key, out T? value)
        where T : class
    {
        if (payload is IEnumerable<KeyValuePair<string, object>> entries)
        {
            foreach (var entry in entries)
            {
                if (StringComparer.Ordinal.Equals(entry.Key, key) &&
                    entry.Value is T matched)
                {
                    value = matched;
                    return true;
                }
            }
        }

        value = null;
        return false;
    }

    private static string? NormalizeOperation(CommandType commandType, string? queryText)
        => commandType switch
        {
            CommandType.StoredProcedure => "CALL",
            CommandType.Text => DatabaseSemantics.NormalizeOperation(null, queryText),
            _ => DatabaseSemantics.NormalizeOperation(null, queryText),
        };

    private static string? GetErrorType(Exception? exception)
        => exception switch
        {
            SqlException sqlException => sqlException.Number.ToString(CultureInfo.InvariantCulture),
            null => null,
            _ => exception.GetType().FullName,
        };

    private static SqlServerEndpoint ParseDataSource(string? dataSource)
    {
        var source = NormalizeEmpty(dataSource);
        if (source is null)
            return default;

        source = StripProtocolPrefix(source);
        var port = ParsePort(ref source);

        var instanceSeparator = source.IndexOf('\\', StringComparison.Ordinal);
        if (instanceSeparator > 0)
            source = source[..instanceSeparator];

        return new SqlServerEndpoint(NormalizeEmpty(source), port);
    }

    private static string StripProtocolPrefix(string source)
    {
        var separator = source.IndexOf(':', StringComparison.Ordinal);
        if (separator <= 0)
            return source;

        var prefix = source[..separator];
        return StringComparer.OrdinalIgnoreCase.Equals(prefix, "tcp") ||
               StringComparer.OrdinalIgnoreCase.Equals(prefix, "np") ||
               StringComparer.OrdinalIgnoreCase.Equals(prefix, "lpc")
            ? source[(separator + 1)..]
            : source;
    }

    private static int? ParsePort(ref string source)
    {
        var separator = source.LastIndexOf(',');
        if (separator <= 0)
            return null;

        if (!int.TryParse(source.AsSpan(separator + 1).Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port))
            return null;

        source = source[..separator];
        return port;
    }

    private static string? NormalizeEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}

internal readonly record struct SqlClientCommand(
    string? Namespace,
    string? Operation,
    string? QuerySummary,
    string? QueryText,
    string? ServerAddress,
    int? ServerPort,
    string? ErrorType);

internal readonly record struct SqlServerEndpoint(string? Address, int? Port);
