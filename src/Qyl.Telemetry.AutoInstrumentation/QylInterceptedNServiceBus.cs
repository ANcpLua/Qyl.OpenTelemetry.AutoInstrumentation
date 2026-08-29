using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>NServiceBus publish and send spans, with the qyl operation-duration metric.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.NServiceBus, QylAttributes.InstrumentationDomainValues.MessagingNServiceBus, MetricIds = [QylAutoInstrumentationIds.NServiceBus])]
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
