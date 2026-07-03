using System.Diagnostics.Metrics;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl database Client Metrics.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylDbClientMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.DatabaseMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(QylMetricNames.DbClientOperationDuration, "s");

    /// <summary>Runs the Get Timestamp runtime helper used by source-generated qyl interceptors.</summary>
    public static long GetTimestamp()
        => OperationDuration.Enabled ? TimeProvider.System.GetTimestamp() : 0;

    /// <summary>Runs the Record Duration runtime helper used by source-generated qyl interceptors.</summary>
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
                new KeyValuePair<string, object?>(QylSemanticAttributes.DbSystemName, GetDbSystemName(instrumentationId)));
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

    private static string GetDbSystemName(string instrumentationId)
        => instrumentationId switch
        {
            QylAutoInstrumentationIds.SqlClient => QylSemanticAttributes.DbSystemMicrosoftSqlServer,
            QylAutoInstrumentationIds.Npgsql => QylSemanticAttributes.DbSystemPostgresql,
            _ => QylSemanticAttributes.DbSystemOtherSql,
        };
}
