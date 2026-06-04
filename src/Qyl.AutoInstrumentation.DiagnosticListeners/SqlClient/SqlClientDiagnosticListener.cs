using System.Diagnostics;
using Qyl.AutoInstrumentation;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.SqlClient;

/// <summary>
/// Subscribes to <c>SqlClientDiagnosticListener</c> — emitted by both <c>System.Data.SqlClient</c>
/// and <c>Microsoft.Data.SqlClient</c>. Direct DB CLIENT spans for SQL Server / Azure SQL.
/// </summary>
public sealed class SqlClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "SqlClientDiagnosticListener";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.db.sqlclient") &&
            !StringComparer.Ordinal.Equals(name, "System.Data.SqlClient.WriteCommandAfter") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandAfter"))
        {
            return;
        }

        var namespaceName = DiagnosticPayloadReader.GetString(payload, "db.namespace", "qyl_demo");
        var operation = DiagnosticPayloadReader.GetString(payload, "db.operation.name", "SELECT");
        var query = DiagnosticPayloadReader.GetString(payload, "db.query.text", "SELECT 1");
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "localhost");

        using var activity = QylActivitySource.Source.StartActivity($"SQL {operation}", ActivityKind.Client);
        activity?.SetTag("qyl.instrumentation.domain", "db.sqlclient");
        activity?.SetTag("db.system", "microsoft.sql_server");
        activity?.SetTag("db.namespace", namespaceName);
        activity?.SetTag("db.operation.name", operation);
        activity?.SetTag("db.query.text", query);
        activity?.SetTag("server.address", serverAddress);
    }
}
