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
        // Skeleton — DB CLIENT span emission arrives next.
    }
}
