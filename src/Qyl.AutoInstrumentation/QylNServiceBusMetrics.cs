using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

public static class QylNServiceBusMetrics
{
    private static readonly Meter Meter = new("NServiceBus.Core");
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("nservicebus.messaging.operation.duration", "s");

    public static long GetTimestamp()
        => TimeProvider.System.GetTimestamp();

    public static void RecordDuration(long startTimestamp)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus))
            return;

        var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
        if (elapsed.TotalSeconds >= 0)
            OperationDuration.Record(elapsed.TotalSeconds);
    }
}
