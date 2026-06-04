using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

public static class QylDbClientMetrics
{
    private static readonly Meter Meter = new("Qyl.AutoInstrumentation.Database");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("db.client.operation.duration", "s");

    public static long GetTimestamp()
        => TimeProvider.System.GetTimestamp();

    public static void RecordDuration(long startTimestamp, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);

        if (!ShouldRecord(instrumentationId))
            return;

        var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
        if (elapsed.TotalSeconds >= 0)
        {
            OperationDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(QylSemanticAttributes.DbSystemName, GetDbSystemName(instrumentationId)));
        }
    }

    private static bool ShouldRecord(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient),
            QylAutoInstrumentationIds.Npgsql => QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Npgsql),
            _ => false,
        };

    private static string GetDbSystemName(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            QylAutoInstrumentationIds.Npgsql => QylSemanticAttributes.DbSystemPostgresql,
            _ => QylSemanticAttributes.DbSystemOtherSql,
        };
}
