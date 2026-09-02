using System.Diagnostics.Metrics;
using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;

namespace Qyl.Telemetry.AutoInstrumentation;

internal static class QylDbClientMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.DatabaseMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(QylMetricNames.DbClientOperationDuration, "s");

    public static long GetTimestamp()
        => OperationDuration.Enabled ? TimeProvider.System.GetTimestamp() : 0;

    public static void RecordDuration(long startTimestamp, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);

        if (startTimestamp is 0 || !IsRecordingEnabled(instrumentationId))
            return;

        var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
        if (elapsed.TotalSeconds >= 0)
        {
            OperationDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(DbAttributes.SystemName, Internal.QylDbActivityPolicy.GetDbSystemName(instrumentationId)));
        }
    }

    internal static bool IsRecordingEnabled(string instrumentationId)
        => OperationDuration.Enabled && ShouldRecord(instrumentationId);

    private static bool ShouldRecord(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient),
            QylAutoInstrumentationIds.Npgsql => QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Npgsql),
            _ => false,
        };
}
