namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Instrumentation Domains.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylInstrumentationDomains
{
    /// <summary>Well-known ASP.NET Core Server value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreServer = "aspnetcore.server";
    /// <summary>Well-known Azure Sdk value used by qyl auto-instrumentation.</summary>
    public const string AzureSdk = "azure.sdk";
    /// <summary>Well-known database Client value used by qyl auto-instrumentation.</summary>
    public const string DbClient = "db.client";
    /// <summary>Well-known database Ef Core value used by qyl auto-instrumentation.</summary>
    public const string DbEfCore = "db.efcore";
    /// <summary>Well-known database Elasticsearch value used by qyl auto-instrumentation.</summary>
    public const string DbElasticsearch = "db.elasticsearch";
    /// <summary>Well-known database Mongo Db value used by qyl auto-instrumentation.</summary>
    public const string DbMongoDb = "db.mongodb";
    /// <summary>Well-known database Redis value used by qyl auto-instrumentation.</summary>
    public const string DbRedis = "db.redis";
    /// <summary>Well-known database Sql Client value used by qyl auto-instrumentation.</summary>
    public const string DbSqlClient = "db.sqlclient";
    /// <summary>Well-known Elastic Transport value used by qyl auto-instrumentation.</summary>
    public const string ElasticTransport = "elastic.transport";
    /// <summary>Well-known Graph Ql value used by qyl auto-instrumentation.</summary>
    public const string GraphQl = "graphql";
    /// <summary>Well-known HTTP Client value used by qyl auto-instrumentation.</summary>
    public const string HttpClient = "http.client";
    /// <summary>Well-known HTTP Server value used by qyl auto-instrumentation.</summary>
    public const string HttpServer = "http.server";
    /// <summary>Well-known HTTP Web Request value used by qyl auto-instrumentation.</summary>
    public const string HttpWebRequest = "http.webrequest";
    /// <summary>Well-known Job Quartz value used by qyl auto-instrumentation.</summary>
    public const string JobQuartz = "job.quartz";
    /// <summary>Well-known Log I Logger value used by qyl auto-instrumentation.</summary>
    public const string LogILogger = "log.ilogger";
    /// <summary>Well-known Log Log4 Net value used by qyl auto-instrumentation.</summary>
    public const string LogLog4Net = "log.log4net";
    /// <summary>Well-known Log N Log value used by qyl auto-instrumentation.</summary>
    public const string LogNLog = "log.nlog";
    /// <summary>Well-known Messaging Kafka value used by qyl auto-instrumentation.</summary>
    public const string MessagingKafka = "messaging.kafka";
    /// <summary>Well-known Messaging Mass Transit value used by qyl auto-instrumentation.</summary>
    public const string MessagingMassTransit = "messaging.masstransit";
    /// <summary>Well-known Messaging N Service Bus value used by qyl auto-instrumentation.</summary>
    public const string MessagingNServiceBus = "messaging.nservicebus";
    /// <summary>Well-known Messaging Rabbit Mq value used by qyl auto-instrumentation.</summary>
    public const string MessagingRabbitMq = "messaging.rabbitmq";
    /// <summary>Well-known Rpc Grpc value used by qyl auto-instrumentation.</summary>
    public const string RpcGrpc = "rpc.grpc";
    /// <summary>Well-known Rpc Wcf Client value used by qyl auto-instrumentation.</summary>
    public const string RpcWcfClient = "rpc.wcf.client";
    /// <summary>Well-known Rpc Wcf Core value used by qyl auto-instrumentation.</summary>
    public const string RpcWcfCore = "rpc.wcf.core";
}
