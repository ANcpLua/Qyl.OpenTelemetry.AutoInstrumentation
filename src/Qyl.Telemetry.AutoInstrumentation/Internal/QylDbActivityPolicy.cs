using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.Internal;

internal static class QylDbActivityPolicy
{
    /// <summary>Commands with an in-flight interceptor-lane activity. Weak on the command so neither
    /// side outlives its natural lifetime: a command in an exporter queue is never pinned by its span,
    /// and a collected command drops its entry automatically.</summary>
    private static readonly ConditionalWeakTable<DbCommand, Activity> InFlightCommands = new();

    public static Activity? StartDbCommandActivity(DbCommand command, string instrumentationId)
    {
        var systemName = GetDbSystemName(instrumentationId);
        var (operation, summary) = QylDbQuerySummary.Describe(command.CommandType, command.CommandText);
        var activity = QylActivityFactory.StartTraceActivity(
            instrumentationId,
            QylSpanNames.Db(summary, systemName),
            ActivityKind.Client,
            QylAttributes.InstrumentationDomainValues.DbClient);
        if (activity is null)
            return null;

        InFlightCommands.AddOrUpdate(command, activity);
        QylActivityTags.SetDb(activity, systemName, operation, summary);

        var databaseName = command.Connection?.Database;
        if (!string.IsNullOrWhiteSpace(databaseName))
            activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);

        QylSensitiveCapturePolicy.SetDbQueryText(activity, command, instrumentationId);
        return activity;
    }

    internal static bool HasCurrentActivityFor(DbCommand command)
    {
        if (!InFlightCommands.TryGetValue(command, out var activity))
            return false;

        if (activity.IsStopped)
        {
            InFlightCommands.Remove(command);
            return false;
        }

        return true;
    }

    internal static string GetDbSystemName(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            QylAutoInstrumentationIds.Sqlite => QylSemanticAttributes.DbSystemSqlite,
            QylAutoInstrumentationIds.Npgsql => QylSemanticAttributes.DbSystemPostgresql,
            QylAutoInstrumentationIds.MySqlConnector => QylSemanticAttributes.DbSystemMysql,
            QylAutoInstrumentationIds.MySqlData => QylSemanticAttributes.DbSystemMysql,
            QylAutoInstrumentationIds.OracleMda => QylSemanticAttributes.DbSystemOracleDb,
            QylAutoInstrumentationIds.AdoNet => QylSemanticAttributes.DbSystemOtherSql,
            _ => throw new ArgumentOutOfRangeException(nameof(instrumentationId), instrumentationId,
                "Unknown DB instrumentation id."),
        };
}
