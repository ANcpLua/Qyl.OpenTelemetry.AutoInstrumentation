using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Elastic.Transport request spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.ElasticTransport, QylAttributes.InstrumentationDomainValues.ElasticTransport)]
[QylIntercept("Elastic.Transport.ITransport", "Request", "RequestAsync", Shape = QylShapes.ElasticTransport, Start = nameof(Request), ObserveAsync = true, ObserveByRefOnly = true)]
public static class QylInterceptedElasticTransport
{
    /// <summary>Starts the client span for the transport request.</summary>
    public static Activity? Request([QylFromMethodName] string methodName)
        => QylElasticActivityPolicy.Start(QylAutoInstrumentationIds.ElasticTransport, QylAttributes.InstrumentationDomainValues.ElasticTransport, methodName);
}
