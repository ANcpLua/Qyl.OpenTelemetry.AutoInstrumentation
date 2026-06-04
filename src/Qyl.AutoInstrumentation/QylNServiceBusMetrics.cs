using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

public static class QylNServiceBusMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.NServiceBusMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>("nservicebus.messaging.operation.duration", "s");

    public static long GetTimestamp()
        => TimeProvider.System.GetTimestamp();

    public static void RecordDuration(long startTimestamp, string operationName)
    {
        if (!QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus))
            return;

        var elapsed = TimeProvider.System.GetElapsedTime(startTimestamp);
        if (elapsed.TotalSeconds >= 0)
        {
            OperationDuration.Record(
                elapsed.TotalSeconds,
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingSystem, QylSemanticAttributes.MessagingSystemNServiceBus),
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingOperationType, QylSemanticAttributes.MessagingOperationTypeSend),
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingOperationName, NormalizeOperation(operationName)));
        }
    }

    private static string NormalizeOperation(string operationName)
        => string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;
}
