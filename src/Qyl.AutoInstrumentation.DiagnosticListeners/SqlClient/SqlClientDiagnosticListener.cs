using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.SqlClient;

/// <summary>
/// Subscribes to the synthetic <c>qyl.db.sqlclient</c> event used by the shared semantic demo.
/// Real Microsoft.Data.SqlClient command events live in <c>Qyl.AutoInstrumentation.SqlClient</c>
/// so non-SqlClient apps do not inherit SqlClient package dependencies or NativeAOT warnings.
/// </summary>
public sealed class SqlClientDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "SqlClientDiagnosticListener";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.SqlClient;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.db.sqlclient"))
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

        using var activity = QylActivitySource.Source.StartActivity(QylActivityNames.SqlClientCommand(operation), ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbSqlClient);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, QylSemanticAttributes.DbSystemMicrosoftSqlServer);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, namespaceName);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, querySummary);
        if (DatabaseSemantics.ShouldWriteQueryText(
                queryText,
                operation,
                QylAutoInstrumentationOptions.Current.SqlClientSetDbStatementForText))
        {
            SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, queryText);
        }

        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, serverAddress);
        DatabaseSemantics.SetError(activity, errorType);
    }
}
