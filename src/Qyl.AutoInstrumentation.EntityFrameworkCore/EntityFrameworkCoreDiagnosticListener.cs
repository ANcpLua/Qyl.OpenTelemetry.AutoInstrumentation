using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.EntityFrameworkCore;

/// <summary>
/// Subscribes to <c>Microsoft.EntityFrameworkCore</c> command events and extracts real EFCore
/// command payload values without IL rewriting.
/// </summary>
public sealed class EntityFrameworkCoreDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Microsoft.EntityFrameworkCore";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.EntityFrameworkCore;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (StringComparer.Ordinal.Equals(name, "qyl.db.efcore"))
        {
            WriteSyntheticActivity(payload);
            return;
        }

        if (!StringComparer.Ordinal.Equals(name, "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.EntityFrameworkCore.Database.Command.CommandError"))
        {
            return;
        }

        if (!EntityFrameworkCorePayloadReader.TryRead(payload, out var command))
            return;

        using var activity = QylActivitySource.Source.StartActivity(QylActivityNames.DbCommand(command.Operation), ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbEfCore);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, command.DbSystem);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, command.Namespace);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, command.Operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, command.QuerySummary);
        if (DatabaseSemantics.ShouldWriteQueryText(
                command.QueryText,
                command.Operation,
                QylAutoInstrumentationOptions.Current.EntityFrameworkCoreSetDbStatementForText))
        {
            SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, command.QueryText);
        }

        DatabaseSemantics.SetError(activity, command.ErrorType);
    }

    private static void WriteSyntheticActivity(object? payload)
    {
        var system = DiagnosticPayloadReader.GetString(payload, QylSemanticAttributes.DbSystemName, "db.system");
        var namespaceName = DiagnosticPayloadReader.GetString(payload, "db.namespace", "db.name");
        var queryText = DiagnosticPayloadReader.GetString(payload, "db.query.text", "db.statement");
        var operation = DatabaseSemantics.NormalizeOperation(
            DiagnosticPayloadReader.GetString(payload, "db.operation.name", "db.operation"),
            queryText);
        var querySummary = DiagnosticPayloadReader.GetString(payload, "db.query.summary");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.Source.StartActivity(QylActivityNames.DbCommand(operation), ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbEfCore);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, system);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, namespaceName);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, querySummary);
        if (DatabaseSemantics.ShouldWriteQueryText(
                queryText,
                operation,
                QylAutoInstrumentationOptions.Current.EntityFrameworkCoreSetDbStatementForText))
        {
            SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, queryText);
        }

        DatabaseSemantics.SetError(activity, errorType);
    }
}
