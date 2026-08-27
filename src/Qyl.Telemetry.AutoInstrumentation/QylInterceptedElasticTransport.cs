using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Elastic.Transport request spans.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.ElasticTransport, QylInstrumentationDomains.ElasticTransport)]
[QylIntercept("Elastic.Transport.ITransport", "Request", "RequestAsync", Shape = QylShapes.ElasticTransport, Start = nameof(Request), ObserveAsync = true, ObserveByRefOnly = true)]
public static class QylInterceptedElasticTransport
{
    /// <summary>Starts the client span for the transport request.</summary>
    public static Activity? Request([QylFromMethodName] string methodName)
        => QylElasticActivityPolicy.Start(QylAutoInstrumentationIds.ElasticTransport, QylInstrumentationDomains.ElasticTransport, methodName);
}
