using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners;
using Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;

namespace Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore;

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
            QylSpanNames.Db(command.QuerySummary, command.DbSystem),
            ActivityKind.Client,
            command.StartTime);
        activity?.SetEndTime((command.StartTime + command.Duration).UtcDateTime);

        SemanticTagWriter.Set(activity, QylAttributes.InstrumentationDomain, QylAttributes.InstrumentationDomainValues.DbEfCore);
        SemanticTagWriter.Set(activity, DbAttributes.SystemName, command.DbSystem);
        SemanticTagWriter.Set(activity, DbAttributes.Namespace, command.Namespace);
        SemanticTagWriter.Set(activity, DbAttributes.OperationName, command.Operation);
        SemanticTagWriter.Set(activity, DbAttributes.QuerySummary, command.QuerySummary);
        if (DatabaseSemantics.ShouldWriteQueryText(
                command.QueryText,
                QylAutoInstrumentationOptions.Current.EntityFrameworkCoreSetDbStatementForText))
        {
            SemanticTagWriter.Set(activity, DbAttributes.QueryText, command.QueryText);
        }

        ErrorStatusSemantics.SetError(activity, command.ErrorType);
    }
}
