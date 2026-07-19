using System.Diagnostics.Metrics;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl N Service Bus Metrics.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
internal static class QylNServiceBusMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.NServiceBusMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(QylMetricNames.NServiceBusMessagingOperationDuration, "s");

    /// <summary>Runs the Get Timestamp runtime helper used by source-generated qyl interceptors.</summary>
    public static long GetTimestamp()
        => IsRecordingEnabled ? TimeProvider.System.GetTimestamp() : 0;

    /// <summary>Runs the Record Duration runtime helper used by source-generated qyl interceptors.</summary>
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
                new KeyValuePair<string, object?>(QylSemanticAttributes.MessagingOperationName, NormalizeOperation(operationName)));
        }
    }

    internal static bool IsRecordingEnabled
        => OperationDuration.Enabled &&
           QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus);

    private static string NormalizeOperation(string operationName)
        => string.Equals(operationName, "Send", StringComparison.Ordinal)
            ? QylSemanticAttributes.MessagingOperationNameSend
            : QylSemanticAttributes.MessagingOperationNamePublish;
}
