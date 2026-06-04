using System.Diagnostics;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.AutoInstrumentation.DiagnosticListeners.EntityFrameworkCore;

/// <summary>
/// Subscribes to <c>Microsoft.EntityFrameworkCore</c> — covers <c>CommandExecuting</c>,
/// <c>CommandExecuted</c>, <c>CommandError</c> across every DB provider EFCore supports.
/// Replaces the substrate's per-EFCore-version CallTarget integration.
/// </summary>
public sealed class EntityFrameworkCoreDiagnosticListener : DiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "Microsoft.EntityFrameworkCore";

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (!StringComparer.Ordinal.Equals(name, "qyl.db.efcore") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted"))
        {
            return;
        }

        var system = DiagnosticPayloadReader.GetString(payload, "db.system");
        var namespaceName = DiagnosticPayloadReader.GetString(payload, "db.namespace", "db.name");
        var queryText = DiagnosticPayloadReader.GetString(payload, "db.query.text", "db.statement");
        var operation = DatabaseSemantics.NormalizeOperation(
            DiagnosticPayloadReader.GetString(payload, "db.operation.name", "db.operation"),
            queryText);
        var querySummary = DiagnosticPayloadReader.GetString(payload, "db.query.summary");
        var errorType = DiagnosticPayloadReader.GetString(payload, "error.type", "exception.type");

        using var activity = QylActivitySource.Source.StartActivity(
            operation is null ? "DB CLIENT" : $"DB {operation}",
            ActivityKind.Client);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, "db.efcore");
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, system);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, namespaceName);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, querySummary);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, queryText);
        DatabaseSemantics.SetError(activity, errorType);
    }
}
