using System.Diagnostics;
using Qyl.AutoInstrumentation;

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

        var system = DiagnosticPayloadReader.GetString(payload, "db.system", "entity_framework");
        var namespaceName = DiagnosticPayloadReader.GetString(payload, "db.namespace", "qyl_demo");
        var operation = DiagnosticPayloadReader.GetString(payload, "db.operation.name", "SELECT");
        var query = DiagnosticPayloadReader.GetString(payload, "db.query.text", "SELECT 1");

        using var activity = QylActivitySource.Source.StartActivity($"DB {operation}", ActivityKind.Client);
        activity?.SetTag("qyl.instrumentation.domain", "db.efcore");
        activity?.SetTag("db.system", system);
        activity?.SetTag("db.namespace", namespaceName);
        activity?.SetTag("db.operation.name", operation);
        activity?.SetTag("db.query.text", query);
    }
}
