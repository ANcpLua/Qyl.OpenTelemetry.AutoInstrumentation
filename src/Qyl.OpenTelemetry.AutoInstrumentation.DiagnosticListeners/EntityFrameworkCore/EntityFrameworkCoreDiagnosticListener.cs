using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.EntityFrameworkCore;

/// <summary>
/// Subscribes to the synthetic <c>qyl.db.efcore</c> event used by the shared semantic demo.
/// Real EFCore command events live in <c>Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore</c> so
/// non-EFCore apps do not inherit EFCore package dependencies or NativeAOT warnings.
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
        if (!StringComparer.Ordinal.Equals(name, "qyl.db.efcore"))
        {
            return;
        }

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
