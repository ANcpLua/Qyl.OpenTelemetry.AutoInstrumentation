using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

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

        var namespaceName = DiagnosticPayloadReader.GetString(payload, "db.namespace", "db.name");
        var queryText = DiagnosticPayloadReader.GetString(payload, "db.query.text", "db.statement");
        var operation = DatabaseSemantics.NormalizeOperation(
            DiagnosticPayloadReader.GetString(payload, "db.operation.name", "db.operation"),
            queryText);
        var querySummary = DiagnosticPayloadReader.GetString(payload, "db.query.summary");
        var serverAddress = DiagnosticPayloadReader.GetString(payload, "server.address", "peer.hostname");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.Source.StartActivity(
            operation is null ? "SQL CLIENT" : $"SQL {operation}",
            ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, "db.sqlclient");
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, "microsoft.sql_server");
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, namespaceName);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, querySummary);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, queryText);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, serverAddress);
        DatabaseSemantics.SetError(activity, errorType);
    }
}
