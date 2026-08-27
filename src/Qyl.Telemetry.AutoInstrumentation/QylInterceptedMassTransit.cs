using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>MassTransit publish and send spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.MassTransit, QylInstrumentationDomains.MessagingMassTransit)]
[QylIntercept("MassTransit.IPublishEndpoint", "Publish", "Send", Shape = QylShapes.MassTransitOperation, Start = nameof(Send))]
[QylIntercept("MassTransit.ISendEndpoint", "Publish", "Send", Shape = QylShapes.MassTransitOperation, Start = nameof(Send))]
[QylIntercept("MassTransit.ISendEndpointProvider", "Publish", "Send", Shape = QylShapes.MassTransitOperation, Start = nameof(Send))]
public static class QylInterceptedMassTransit
{
    /// <summary>Starts the producer span named after the intercepted operation.</summary>
    public static Activity? Send([QylFromMethodName] string operationName)
        => QylMessagingActivityPolicy.StartMassTransitActivity(operationName);
}
