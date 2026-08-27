using System.Diagnostics.Metrics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation;

internal static class QylNServiceBusMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.NServiceBusMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(QylMetricNames.NServiceBusMessagingOperationDuration, "s");

    public static long GetTimestamp()
        => IsRecordingEnabled ? TimeProvider.System.GetTimestamp() : 0;

    public static void RecordDuration(long startTimestamp, string operationName)
    {
        if (startTimestamp is 0 || !IsRecordingEnabled)
            return;

        var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
        if (elapsed.TotalSeconds >= 0)
        {
            OperationDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemNServiceBus),
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend),
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingOperationName, QylMessagingActivityPolicy.OperationName(operationName)));
        }
    }

    internal static bool IsRecordingEnabled
        => OperationDuration.Enabled &&
           QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus);
}
