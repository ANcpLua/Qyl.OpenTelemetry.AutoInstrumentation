using System.Diagnostics;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners;
using Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.Semantics;
using Qyl.OpenTelemetry.AutoInstrumentation.Internal;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SqlClient;

/// <summary>
/// Subscribes to Microsoft.Data.SqlClient command events and extracts real command payload values
/// without IL rewriting or reflection.
/// </summary>
internal sealed class SqlClientDiagnosticListener : QylDiagnosticListenerSubscriber
{
    /// <inheritdoc/>
    protected override string ListenerName => "SqlClientDiagnosticListener";

    /// <inheritdoc/>
    protected override QylAutoInstrumentationSignal Signal => QylAutoInstrumentationSignal.Traces;

    /// <inheritdoc/>
    protected override string InstrumentationId => QylAutoInstrumentationIds.SqlClient;

    /// <summary>In-flight command start timestamps, keyed by the diagnostic OperationId. Entries are
    /// removed on the matching After/Error event; the size cap guards against events whose completion
    /// never fires (killed connections) from accumulating forever.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Guid, long> PendingOperations = new();

    private const int PendingOperationsCap = 10_000;

    /// <inheritdoc/>
    protected override void OnEvent(string name, object? payload)
    {
        if (StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandBefore"))
        {
            if (SqlClientPayloadReader.TryReadOperationStart(payload, out var operationId, out var startTimestamp) &&
                PendingOperations.Count < PendingOperationsCap)
            {
                PendingOperations[operationId] = startTimestamp;
            }

            return;
        }

        var isSuccess = StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandAfter");
        var isError = StringComparer.Ordinal.Equals(name, "Microsoft.Data.SqlClient.WriteCommandError");

        if (!isSuccess && !isError)
            return;

        if (!SqlClientPayloadReader.TryRead(payload, isError, out var command))
            return;

        var duration = TimeSpan.Zero;
        if (command.OperationId is { } operationKey &&
            PendingOperations.TryRemove(operationKey, out var beforeTimestamp) &&
            command.Timestamp is { } afterTimestamp &&
            afterTimestamp > beforeTimestamp)
        {
            duration = TimeSpan.FromTicks(
                (long)((afterTimestamp - beforeTimestamp) * ((double)TimeSpan.TicksPerSecond / Stopwatch.Frequency)));
        }

        if (QylDbActivityPolicy.HasCurrentActivityFor(command.Command))
            return;

        var endTime = TimeProvider.System.GetUtcNow();
        using var activity = QylActivitySource.StartAt(
            QylActivityNames.SqlClientCommand(command.Operation),
            ActivityKind.Client,
            endTime - duration);
        activity?.SetEndTime(endTime.UtcDateTime);

        SemanticTagWriter.Set(activity, SemanticAttributes.QylInstrumentationDomain, QylInstrumentationDomains.DbSqlClient);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbSystem, QylSemanticAttributes.DbSystemMicrosoftSqlServer);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbNamespace, command.Namespace);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbOperationName, command.Operation);
        SemanticTagWriter.Set(activity, SemanticAttributes.DbQuerySummary, command.QuerySummary);
        if (DatabaseSemantics.ShouldWriteQueryText(
                command.QueryText,
                command.Operation,
                QylAutoInstrumentationOptions.Current.SqlClientSetDbStatementForText))
        {
            SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText, command.QueryText);
        }

        SemanticTagWriter.Set(activity, SemanticAttributes.ServerAddress, command.ServerAddress);
        SemanticTagWriter.Set(activity, SemanticAttributes.ServerPort, command.ServerPort);
        DatabaseSemantics.SetError(activity, command.ErrorType);
    }

}
