using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.SqlClient;

/// <summary>
/// Subscribes to Microsoft.Data.SqlClient command events and extracts real command payload values
/// without IL rewriting or reflection.
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
        var isSuccess = StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandAfter");
        var isError = StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandError");

        if (!isSuccess && !isError)
            return;

        if (!SqlClientPayloadReader.TryRead(payload, isError, out var command))
            return;

        using var activity = QylActivitySource.Source.StartActivity(
            command.Operation is null ? "SQL CLIENT" : $"SQL {command.Operation}",
            ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, "db.sqlclient");
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, QylSemanticAttributes.DbSystemMicrosoftSqlServer);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, command.Namespace);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, command.Operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, command.QuerySummary);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, command.QueryText);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, command.ServerAddress);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerPort, command.ServerPort);
        DatabaseSemantics.SetError(activity, command.ErrorType);
    }
}
