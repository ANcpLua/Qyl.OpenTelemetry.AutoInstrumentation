namespace Qyl.Telemetry.AutoInstrumentation;

/// <summary>
/// Declares a signal an instrumentation id owns outside the interceptor lane, so the enabled-set
/// the environment toggles bind to is generated from declarations rather than retyped.
/// </summary>
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true, Inherited = false)]
[JetBrains.Annotations.MeansImplicitUse]
internal sealed class QylSignalAttribute : Attribute
{
    // The generator reads constructor arguments from Roslyn AttributeData; no runtime
    // attribute instance or duplicate property storage is needed.
    public QylSignalAttribute([JetBrains.Annotations.UsedImplicitly] string instrumentationId,
        [JetBrains.Annotations.UsedImplicitly] QylAutoInstrumentationSignal signal) { }
}

/// <summary>
/// The signals owned by the listener, middleware, meter-subscription, and library-native lanes.
/// Interceptor-lane signals are declared on their <c>QylIntercepted*</c> class instead.
/// </summary>
[QylSignal(QylAutoInstrumentationIds.AspNetCore, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.AspNetCore, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.Azure, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.ElasticTransport, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.Elasticsearch, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.EntityFrameworkCore, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.GraphQl, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.GrpcNetClient, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.HttpClient, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.HttpClient, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.MassTransit, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MicrosoftAgentsAi, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MicrosoftAgentsAi, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.MicrosoftAgentsAiWorkflows, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MicrosoftExtensionsAi, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MicrosoftExtensionsAi, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.ModelContextProtocol, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MongoDb, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MySqlConnector, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.MySqlData, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.Npgsql, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.NServiceBus, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.NetRuntime, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.OracleMda, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.Process, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.Quartz, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.Quartz, QylAutoInstrumentationSignal.Metrics)]
[QylSignal(QylAutoInstrumentationIds.RabbitMq, QylAutoInstrumentationSignal.Traces)]
[QylSignal(QylAutoInstrumentationIds.WcfCore, QylAutoInstrumentationSignal.Traces)]
internal static class QylNativeSignals;
