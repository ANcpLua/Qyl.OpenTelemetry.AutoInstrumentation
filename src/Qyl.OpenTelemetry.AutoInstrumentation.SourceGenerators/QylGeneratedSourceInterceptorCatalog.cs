using System.Collections.Immutable;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private static ImmutableArray<InterceptorMatcherDescriptor> CreateGeneratedMatcherDescriptors()
        => ImmutableArray.Create(
            new InterceptorMatcherDescriptor("HttpClient", "global::System.Net.Http.HttpClient", TryGetHttpClientInvocation),
            new InterceptorMatcherDescriptor("HttpWebRequest", "global::System.Net.HttpWebRequest", TryGetHttpWebRequestInvocation),
            new InterceptorMatcherDescriptor("AspNetCoreRequestDelegate", "global::Microsoft.AspNetCore.Http.RequestDelegate", TryGetAspNetCoreRequestDelegateInvocation),
            new InterceptorMatcherDescriptor("AspNetCoreEndpointMap", "global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions", TryGetAspNetCoreEndpointMapInvocation),
            new InterceptorMatcherDescriptor("MeterProviderBuilderAddMeter", "global::OpenTelemetry.Metrics.MeterProviderBuilder", TryGetMeterProviderBuilderAddMeterInvocation),
            new InterceptorMatcherDescriptor("AzureClient", "Azure.*Client", TryGetAzureClientInvocation),
            new InterceptorMatcherDescriptor("Elastic", "Elastic.Clients.Elasticsearch.*Client|Elastic.Transport.ITransport", TryGetElasticInvocation),
            new InterceptorMatcherDescriptor("WcfClient", "global::System.ServiceModel.ClientBase<TChannel>", TryGetWcfClientInvocation),
            new InterceptorMatcherDescriptor("GrpcNetClientUnary", "global::Grpc.Core.ClientBase<T>", TryGetGrpcNetClientAsyncUnaryInvocation),
            new InterceptorMatcherDescriptor("GrpcNetClientStreaming", "global::Grpc.Core.ClientBase<T>", TryGetGrpcNetClientStreamingInvocation),
            new InterceptorMatcherDescriptor("Kafka", "Confluent.Kafka.IProducer<TKey,TValue>|Confluent.Kafka.IConsumer<TKey,TValue>", TryGetKafkaInvocation),
            new InterceptorMatcherDescriptor("MassTransit", "MassTransit.IPublishEndpoint|MassTransit.ISendEndpoint|MassTransit.ISendEndpointProvider", TryGetMassTransitInvocation),
            new InterceptorMatcherDescriptor("NServiceBus", "NServiceBus.IMessageSession|NServiceBus.IMessageHandlerContext", TryGetNServiceBusInvocation),
            new InterceptorMatcherDescriptor("Quartz", "Quartz.IJob", TryGetQuartzInvocation),
            new InterceptorMatcherDescriptor("StackExchangeRedis", "StackExchange.Redis.IDatabase", TryGetStackExchangeRedisInvocation),
            new InterceptorMatcherDescriptor("GraphQL", "GraphQL.IDocumentExecuter", TryGetGraphQlInvocation),
            new InterceptorMatcherDescriptor("EntityFrameworkCoreDbContext", "global::Microsoft.EntityFrameworkCore.DbContext", TryGetEntityFrameworkCoreDbContextInvocation),
            new InterceptorMatcherDescriptor("EntityFrameworkCoreQueryable", "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions", TryGetEntityFrameworkCoreQueryableInvocation),
            new InterceptorMatcherDescriptor("MongoDb", "MongoDB.Driver.IMongoCollection<TDocument>", TryGetMongoDbInvocation),
            new InterceptorMatcherDescriptor("DbCommand", "global::System.Data.Common.DbCommand", TryGetDbCommandInvocation),
            new InterceptorMatcherDescriptor("RabbitMq", "RabbitMQ.Client.IModel|RabbitMQ.Client.IChannel", TryGetRabbitMqInvocation),
            new InterceptorMatcherDescriptor("LoggerExtensions", "global::Microsoft.Extensions.Logging.LoggerExtensions", TryGetLoggerExtensionInvocation),
            new InterceptorMatcherDescriptor("ILogger", "global::Microsoft.Extensions.Logging.ILogger", TryGetLoggerInvocation),
            new InterceptorMatcherDescriptor("NLog", "NLog.Logger", TryGetNLogInvocation),
            new InterceptorMatcherDescriptor("Log4Net", "log4net.ILog|log4net.Core.ILogger", TryGetLog4NetInvocation));

    private static ImmutableArray<InterceptorEmissionDescriptor> CreateGeneratedEmissionDescriptors()
        => ImmutableArray.Create(
            new InterceptorEmissionDescriptor(InterceptorKind.HttpClient, new ForwardingInterceptorBodyDescriptor("HttpClient", "client", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedHttpClient", ReceiverTypeOverride: "global::System.Net.Http.HttpClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.HttpWebRequest, new HttpWebRequestBodyDescriptor("HttpWebRequest", "request", "global::System.Net.HttpWebRequest", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedHttpWebRequest", "GetStartTimeUtc", "StartActivity", "RecordResult", "RecordException")),
            new InterceptorEmissionDescriptor(InterceptorKind.AspNetCoreRequestDelegate, new ForwardingInterceptorBodyDescriptor("AspNetCoreRequestDelegate", "requestDelegate", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedAspNetCore", HelperMethodName: "InvokeAsync")),
            new InterceptorEmissionDescriptor(InterceptorKind.AspNetCoreEndpointMap, new ForwardingInterceptorBodyDescriptor("AspNetCoreEndpointMap", "endpoints", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedAspNetCore")),
            new InterceptorEmissionDescriptor(InterceptorKind.MeterProviderBuilderAddMeter, new MeterProviderBuilderBodyDescriptor("MeterProviderBuilder", "builder", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylMetricMeters.GetEnabledMeterNames()")),
            new InterceptorEmissionDescriptor(InterceptorKind.AzureClient, new TraceInterceptorBodyDescriptor("AzureClient", "client", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedAzure", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.ElasticsearchClient, new TraceInterceptorBodyDescriptor("Elastic", "client", MethodPrefixKind: TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName, AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedElastic.ObserveAsync", TraceAsyncObservationCondition.AsyncWithByRefParameters), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedElastic", "StartActivity", "RecordException", TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.ElasticTransport, new TraceInterceptorBodyDescriptor("Elastic", "client", MethodPrefixKind: TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName, AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedElastic.ObserveAsync", TraceAsyncObservationCondition.AsyncWithByRefParameters), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedElastic", "StartActivity", "RecordException", TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.WcfClient, new TraceInterceptorBodyDescriptor("WcfClient", "client", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedWcfClient", "StartActivity", "RecordException", TraceStartActivityArgumentKind.ReceiverTypeAndTargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncUnaryCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.Unary, "GrpcNetClientAsyncUnary", "client", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncServerStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.ServerStreaming, "GrpcNetClientAsyncServerStreaming", "client", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncClientStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.ClientStreaming, "GrpcNetClientAsyncClientStreaming", "client", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall, new GrpcClientBodyDescriptor(GrpcClientCallShape.DuplexStreaming, "GrpcNetClientAsyncDuplexStreaming", "client", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGrpcNetClient")),
            new InterceptorEmissionDescriptor(InterceptorKind.KafkaProducer, new TraceInterceptorBodyDescriptor("KafkaProducer", "producer", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedKafka", "StartProducerActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.KafkaConsumer, new TraceInterceptorBodyDescriptor("KafkaConsumer", "consumer", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedKafka", "StartConsumerActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.MassTransitMessageOperation, new TraceInterceptorBodyDescriptor("MassTransit", "endpoint", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedMassTransit", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.NServiceBusMessageOperation, new TraceInterceptorBodyDescriptor("NServiceBus", "endpoint", DurationMetric: new TraceDurationMetricDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedNServiceBus", "GetTimestamp", "RecordDuration", TraceDurationMetricArgumentKind.TargetMethodName), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedNServiceBus", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.QuartzJobExecute, new TraceInterceptorBodyDescriptor("Quartz", "job", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedQuartz.ObserveAsync"), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedQuartz", "StartActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.StackExchangeRedisCommandAsync, new TraceInterceptorBodyDescriptor("StackExchangeRedis", "database", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedRedis", "StartCommandActivity", "RecordException", TraceStartActivityArgumentKind.RedisOperationName))),
            new InterceptorEmissionDescriptor(InterceptorKind.GraphQlDocumentExecuter, new TraceInterceptorBodyDescriptor("GraphQl", "executer", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGraphQl.ObserveAsync"), ActivityEnrichment: new TraceActivityEnrichmentDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGraphQl", "RecordExecutionOptions", TraceActivityEnrichmentArgumentKind.GraphQlExecutionOptions), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGraphQl", "StartActivity", "RecordException"))),
            new InterceptorEmissionDescriptor(InterceptorKind.EntityFrameworkCoreDbContext, new TraceInterceptorBodyDescriptor("EntityFrameworkCoreDbContext", "dbContext", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedEntityFrameworkCore", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.EntityFrameworkCoreQueryable, new TraceInterceptorBodyDescriptor("EntityFrameworkCoreQueryable", "query", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedEntityFrameworkCore", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.MongoDbCollection, new TraceInterceptorBodyDescriptor("MongoDb", "collection", AsyncObservation: new TraceAsyncObservationDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedMongoDb.ObserveAsync"), RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedMongoDb", "StartActivity", "RecordException", TraceStartActivityArgumentKind.TargetMethodName))),
            new InterceptorEmissionDescriptor(InterceptorKind.DbCommand, new DbCommandBodyDescriptor("DbCommand", "command", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedDbCommand", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedDbCommand", "GetTimestamp", "StartActivity", "ObserveAsync", "RecordException", "RecordDuration")),
            new InterceptorEmissionDescriptor(InterceptorKind.RabbitMqBasicPublish, new TraceInterceptorBodyDescriptor("RabbitMq", "channel", RuntimeHelper: new TraceRuntimeHelperDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedRabbitMq", "StartPublishActivity", "RecordException", TraceStartActivityArgumentKind.RabbitMqExchange))),
            new InterceptorEmissionDescriptor(InterceptorKind.ILoggerExtensionLog, new LoggerBodyDescriptor(LoggerInterceptorBodyKind.LoggerExtensionLog, "LoggerExtensions", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedLogger")),
            new InterceptorEmissionDescriptor(InterceptorKind.ILoggerLog, new LoggerBodyDescriptor(LoggerInterceptorBodyKind.ILoggerLog, "ILogger_Log", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedLogger")),
            new InterceptorEmissionDescriptor(InterceptorKind.NLogLogger, new ExternalLoggerBodyDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedExternalLogger", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInstrumentationDomains.LogNLog")),
            new InterceptorEmissionDescriptor(InterceptorKind.Log4NetLogger, new ExternalLoggerBodyDescriptor("global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedExternalLogger", "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInstrumentationDomains.LogLog4Net")));

    /// <summary>A single interceptor matcher projected to its receiver-type surface.</summary>
    internal readonly struct InterceptorReceiverSurfaceEntry
    {
        public InterceptorReceiverSurfaceEntry(string name, string receiverType)
        {
            Name = name;
            ReceiverType = receiverType;
        }

        /// <summary>The matcher name (e.g. <c>"Kafka"</c>).</summary>
        public string Name { get; }

        /// <summary>The receiver type or wildcard/pipe pattern the matcher targets.</summary>
        public string ReceiverType { get; }
    }

    /// <summary>
    /// Projects every interceptor matcher to its receiver-type surface for the Telemetry Capability
    /// Graph. Reading <see cref="InterceptorMatcherDescriptor.ReceiverTypePattern"/> here makes the
    /// curated receiver registry a live, machine-readable output instead of dead metadata: the
    /// wildcard/pipe patterns (e.g. Azure, Elastic, Kafka) are not recoverable from the matcher
    /// delegates, so surfacing them documents the exact receiver surface qyl intercepts.
    /// </summary>
    internal static ImmutableArray<InterceptorReceiverSurfaceEntry> GetInterceptorReceiverSurface()
    {
        var builder = ImmutableArray.CreateBuilder<InterceptorReceiverSurfaceEntry>(s_matcherDescriptors.Length);
        foreach (var descriptor in s_matcherDescriptors)
            builder.Add(new InterceptorReceiverSurfaceEntry(descriptor.Name, descriptor.ReceiverTypePattern));

        return builder.MoveToImmutable();
    }
}
