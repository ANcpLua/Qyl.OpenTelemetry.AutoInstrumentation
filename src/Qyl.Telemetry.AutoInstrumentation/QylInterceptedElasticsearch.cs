using System.Diagnostics;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;

namespace Qyl.Telemetry.AutoInstrumentation.GeneratedCode;

/// <summary>Elastic.Clients.Elasticsearch client request spans.</summary>
[System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
[QylIntegration(QylAutoInstrumentationIds.Elasticsearch, QylAttributes.InstrumentationDomainValues.DbElasticsearch)]
[QylIntercept("", Shape = QylShapes.ElasticsearchClient, Start = nameof(Request), ObserveAsync = true, ObserveByRefOnly = true)]
public static class QylInterceptedElasticsearch
{
    /// <summary>Starts the client span for the client method's normalized operation.</summary>
    public static Activity? Request([QylFromMethodName] string methodName)
        => QylElasticActivityPolicy.Start(QylAutoInstrumentationIds.Elasticsearch, QylAttributes.InstrumentationDomainValues.DbElasticsearch, methodName);
}
