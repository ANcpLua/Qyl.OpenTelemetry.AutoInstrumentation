namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Metric Names.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
internal static class QylMetricNames
{
    /// <summary>Well-known database Client Operation Duration value used by qyl auto-instrumentation.</summary>
    public const string DbClientOperationDuration = "db.client.operation.duration";
    /// <summary>Well-known N Service Bus Messaging Operation Duration value used by qyl auto-instrumentation.</summary>
    public const string NServiceBusMessagingOperationDuration = "nservicebus.messaging.operation.duration";

}
