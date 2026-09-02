using System.Data.Common;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;

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
            activity.SetTag(DbAttributes.Namespace, databaseName);

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
            QylAutoInstrumentationIds.SqlClient => DbAttributes.SystemNameValues.MicrosoftSqlServer,
            QylAutoInstrumentationIds.Sqlite => DbIncubatingAttributes.SystemNameValues.Sqlite,
            QylAutoInstrumentationIds.Npgsql => DbAttributes.SystemNameValues.Postgresql,
            QylAutoInstrumentationIds.MySqlConnector => DbAttributes.SystemNameValues.Mysql,
            QylAutoInstrumentationIds.MySqlData => DbAttributes.SystemNameValues.Mysql,
            QylAutoInstrumentationIds.OracleMda => DbIncubatingAttributes.SystemNameValues.OracleDb,
            QylAutoInstrumentationIds.AdoNet => DbIncubatingAttributes.SystemNameValues.OtherSql,
            _ => throw new ArgumentOutOfRangeException(nameof(instrumentationId), instrumentationId,
                "Unknown DB instrumentation id."),
        };
}
