using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;

namespace Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore;

/// <summary>
/// Subscribes to <c>Microsoft.EntityFrameworkCore</c> command events and extracts real EFCore
/// command payload values without IL rewriting.
/// </summary>
internal sealed class EntityFrameworkCoreDiagnosticListener : QylDiagnosticListenerSubscriber
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
        if (!StringComparer.Ordinal.Equals(name, "Microsoft.EntityFrameworkCore.Database.Command.CommandExecuted") &&
            !StringComparer.Ordinal.Equals(name, "Microsoft.EntityFrameworkCore.Database.Command.CommandError"))
        {
            return;
        }

        if (!EntityFrameworkCorePayloadReader.TryRead(payload, out var command))
            return;

        using var activity = QylActivitySource.StartAt(
            QylActivityNames.DbCommand(command.Operation),
            ActivityKind.Client,
            command.StartTime);
        activity?.SetEndTime((command.StartTime + command.Duration).UtcDateTime);

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
}
