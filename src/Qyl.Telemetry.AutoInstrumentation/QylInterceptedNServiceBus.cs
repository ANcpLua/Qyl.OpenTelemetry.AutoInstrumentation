using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>NServiceBus publish and send spans, with the qyl operation-duration metric.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.NServiceBus, QylInstrumentationDomains.MessagingNServiceBus, MetricIds = [QylAutoInstrumentationIds.NServiceBus])]
[QylIntercept("NServiceBus.IMessageSession", "Publish", "Send", Shape = QylShapes.NServiceBusOperation, Start = nameof(Send), Metric = nameof(RecordDuration))]
[QylIntercept("NServiceBus.IMessageHandlerContext", "Publish", "Send", Shape = QylShapes.NServiceBusOperation, Start = nameof(Send), Metric = nameof(RecordDuration))]
public static class QylInterceptedNServiceBus
{
    /// <summary>Starts the producer span named after the intercepted operation.</summary>
    public static Activity? Send([QylFromMethodName] string operationName)
        => QylMessagingActivityPolicy.StartNServiceBusActivity(operationName);

    /// <summary>Reads the metric start timestamp, or zero when the metric is not recording.</summary>
    public static long GetTimestamp()
        => QylNServiceBusMetrics.GetTimestamp();

    /// <summary>Records the operation duration since <paramref name="startTimestamp"/>.</summary>
    public static void RecordDuration(long startTimestamp, [QylFromMethodName] string operationName)
        => QylNServiceBusMetrics.RecordDuration(startTimestamp, operationName);
}
