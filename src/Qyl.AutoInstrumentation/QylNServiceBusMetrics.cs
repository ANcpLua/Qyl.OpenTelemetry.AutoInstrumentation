using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl N Service Bus Metrics.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
/// <example><code>var apiType = typeof(QylNServiceBusMetrics);</code></example>
public static class QylNServiceBusMetrics
{
    private static readonly Meter Meter = new(QylMetricMeters.NServiceBusMeterName);
    private static readonly Histogram<double> OperationDuration = Meter.CreateHistogram<double>(QylMetricNames.NServiceBusMessagingOperationDuration, "s");

    /// <summary>Runs the Get Timestamp runtime helper used by source-generated qyl interceptors.</summary>
    public static long GetTimestamp()
        => TimeProvider.System.GetTimestamp();

    /// <summary>Runs the Record Duration runtime helper used by source-generated qyl interceptors.</summary>
    public static void RecordDuration(long startTimestamp, string operationName)
    {
        if (!OperationDuration.Enabled ||
            !QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus))
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
