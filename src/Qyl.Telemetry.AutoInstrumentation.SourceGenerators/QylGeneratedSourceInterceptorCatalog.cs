using System.Collections.Immutable;

namespace Qyl.Telemetry.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private static ImmutableArray<InterceptorMatcherDescriptor> CreateGeneratedMatcherDescriptors()
        => ImmutableArray.Create(
            new InterceptorMatcherDescriptor(TryGetHttpClientInvocation),
            new InterceptorMatcherDescriptor(TryGetElasticInvocation),
            new InterceptorMatcherDescriptor(TryGetWcfClientInvocation),
            new InterceptorMatcherDescriptor(TryGetGrpcNetClientAsyncUnaryInvocation),
            new InterceptorMatcherDescriptor(TryGetGrpcNetClientStreamingInvocation),
            new InterceptorMatcherDescriptor(TryGetKafkaInvocation),
            new InterceptorMatcherDescriptor(TryGetMassTransitInvocation),
            new InterceptorMatcherDescriptor(TryGetNServiceBusInvocation),
            new InterceptorMatcherDescriptor(TryGetQuartzInvocation),
            new InterceptorMatcherDescriptor(TryGetStackExchangeRedisInvocation),
            new InterceptorMatcherDescriptor(TryGetGraphQlInvocation),
            new InterceptorMatcherDescriptor(TryGetMongoDbInvocation),
            new InterceptorMatcherDescriptor(TryGetDbCommandInvocation),
            new InterceptorMatcherDescriptor(TryGetRabbitMqInvocation),
            new InterceptorMatcherDescriptor(TryGetLoggerExtensionInvocation),
            new InterceptorMatcherDescriptor(TryGetLoggerInvocation),
            new InterceptorMatcherDescriptor(TryGetNLogInvocation),
            new InterceptorMatcherDescriptor(TryGetLog4NetInvocation));

    private static ImmutableArray<InterceptorEmissionDescriptor> CreateGeneratedEmissionDescriptors()
        => ImmutableArray.Create(
            new InterceptorEmissionDescriptor(InterceptorKind.HttpClient, new ForwardingInterceptorBodyDescriptor("HttpClient", "client", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedHttpClient", ReceiverTypeOverride: "global::System.Net.Http.HttpClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.ElasticsearchClient, new TraceInterceptorBodyDescriptor("Elastic", "client", MethodPrefixKind: TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName, AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedElastic.ObserveAsync", TraceAsyncObservationCondition.AsyncWithByRefParameters), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedElastic", "StartActivity", "RecordException", TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.ElasticTransport, new TraceInterceptorBodyDescriptor("Elastic", "client", MethodPrefixKind: TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName, AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedElastic.ObserveAsync", TraceAsyncObservationCondition.AsyncWithByRefParameters), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedElastic", "StartActivity", "RecordException", TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.WcfClient, new TraceInterceptorBodyDescriptor("WcfClient", "client", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedWcfClient", "StartActivity", "RecordException", TraceStartActivityArgumentKind.ReceiverTypeAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncUnaryCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.Unary, "GrpcNetClientAsyncUnary", "client", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncServerStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.ServerStreaming, "GrpcNetClientAsyncServerStreaming", "client", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncClientStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.ClientStreaming, "GrpcNetClientAsyncClientStreaming", "client", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.DuplexStreaming, "GrpcNetClientAsyncDuplexStreaming", "client", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.KafkaProducer, new TraceInterceptorBodyDescriptor("KafkaProducer", "producer", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedKafka", "StartProducerActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.KafkaConsumer, new TraceInterceptorBodyDescriptor("KafkaConsumer", "consumer", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedKafka", "StartConsumerActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.MassTransitMessageOperation, new TraceInterceptorBodyDescriptor("MassTransit", "endpoint", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedMassTransit", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.NServiceBusMessageOperation, new TraceInterceptorBodyDescriptor("NServiceBus", "endpoint", DurationMetric: new TraceDurationMetricDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedNServiceBus", "GetTimestamp", "RecordDuration", TraceDurationMetricArgumentKind.TargetMethodName), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedNServiceBus", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.QuartzJobExecute, new TraceInterceptorBodyDescriptor("Quartz", "job", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedQuartz.ObserveAsync"), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedQuartz", "StartActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.StackExchangeRedisCommandAsync, new TraceInterceptorBodyDescriptor("StackExchangeRedis", "database", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedRedis", "StartCommandActivity", "RecordException", TraceStartActivityArgumentKind.RedisOperationName))),
            new InterceptorEmissionDescriptor(InterceptorKind.GraphQlDocumentExecuter, new TraceInterceptorBodyDescriptor("GraphQl", "executer", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGraphQl.ObserveAsync"), ActivityEnrichment: new TraceActivityEnrichmentDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGraphQl", "RecordExecutionOptions", TraceActivityEnrichmentArgumentKind.GraphQlExecutionOptions), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedGraphQl", "StartActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.MongoDbCollection, new TraceInterceptorBodyDescriptor("MongoDb", "collection", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedMongoDb.ObserveAsync"), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedMongoDb", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.DbCommand, new DbCommandBodyDescriptor("DbCommand", "command", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedDbCommand", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedDbCommand", "GetTimestamp", "StartActivity", "ObserveAsync", "RecordException", "RecordDuration")),
            new InterceptorEmissionDescriptor(InterceptorKind.RabbitMqBasicPublish, new TraceInterceptorBodyDescriptor("RabbitMq", "channel", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedRabbitMq", "StartPublishActivity", "RecordException", TraceStartActivityArgumentKind.RabbitMqExchange))),
            new InterceptorEmissionDescriptor(InterceptorKind.ILoggerExtensionLog, new LoggerBodyDescriptor(LoggerInterceptorBodyKind.LoggerExtensionLog, "LoggerExtensions", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedLogger")),
            new InterceptorEmissionDescriptor(InterceptorKind.ILoggerLog, new LoggerBodyDescriptor(LoggerInterceptorBodyKind.ILoggerLog, "ILogger_Log", "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedLogger")),
            new InterceptorEmissionDescriptor(InterceptorKind.NLogLogger, new ExternalLoggerBodyDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedExternalLogger", "\"log.nlog\"")),
            new InterceptorEmissionDescriptor(InterceptorKind.Log4NetLogger, new ExternalLoggerBodyDescriptor("global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedExternalLogger", "\"log.log4net\"")));

}
