namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Auto Instrumentation Signal.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public enum QylAutoInstrumentationSignal
{
    /// <summary>Represents the Traces qyl auto-instrumentation signal.</summary>
    Traces,
    /// <summary>Represents the Metrics qyl auto-instrumentation signal.</summary>
    Metrics,
    /// <summary>Represents the Logs qyl auto-instrumentation signal.</summary>
    Logs,
}

/// <summary>Defines the qyl auto-instrumentation surface for qyl Auto Instrumentation Ids.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylAutoInstrumentationIds
{
    /// <summary>Well-known Ado Net value used by qyl auto-instrumentation.</summary>
    public const string AdoNet = "ADONET";
    /// <summary>Well-known ASP.NET value used by qyl auto-instrumentation.</summary>
    public const string AspNet = "ASPNET";
    /// <summary>Well-known ASP.NET Core value used by qyl auto-instrumentation.</summary>
    public const string AspNetCore = "ASPNETCORE";
    /// <summary>Well-known Azure value used by qyl auto-instrumentation.</summary>
    public const string Azure = "AZURE";
    /// <summary>Well-known Elasticsearch value used by qyl auto-instrumentation.</summary>
    public const string Elasticsearch = "ELASTICSEARCH";
    /// <summary>Well-known Elastic Transport value used by qyl auto-instrumentation.</summary>
    public const string ElasticTransport = "ELASTICTRANSPORT";
    /// <summary>Well-known Entity Framework Core value used by qyl auto-instrumentation.</summary>
    public const string EntityFrameworkCore = "ENTITYFRAMEWORKCORE";
    /// <summary>Well-known Graph Ql value used by qyl auto-instrumentation.</summary>
    public const string GraphQl = "GRAPHQL";
    /// <summary>Well-known gRPC Net Client value used by qyl auto-instrumentation.</summary>
    public const string GrpcNetClient = "GRPCNETCLIENT";
    /// <summary>Well-known HTTP Client value used by qyl auto-instrumentation.</summary>
    public const string HttpClient = "HTTPCLIENT";
    /// <summary>Well-known Kafka value used by qyl auto-instrumentation.</summary>
    public const string Kafka = "KAFKA";
    /// <summary>Well-known Mass Transit value used by qyl auto-instrumentation.</summary>
    public const string MassTransit = "MASSTRANSIT";
    /// <summary>Well-known Mongo Db value used by qyl auto-instrumentation.</summary>
    public const string MongoDb = "MONGODB";
    /// <summary>Well-known My Sql Connector value used by qyl auto-instrumentation.</summary>
    public const string MySqlConnector = "MYSQLCONNECTOR";
    /// <summary>Well-known My Sql Data value used by qyl auto-instrumentation.</summary>
    public const string MySqlData = "MYSQLDATA";
    /// <summary>Well-known Net Runtime value used by qyl auto-instrumentation.</summary>
    public const string NetRuntime = "NETRUNTIME";
    /// <summary>Well-known Npgsql value used by qyl auto-instrumentation.</summary>
    public const string Npgsql = "NPGSQL";
    /// <summary>Well-known N Service Bus value used by qyl auto-instrumentation.</summary>
    public const string NServiceBus = "NSERVICEBUS";
    /// <summary>Well-known Oracle Mda value used by qyl auto-instrumentation.</summary>
    public const string OracleMda = "ORACLEMDA";
    /// <summary>Well-known Process value used by qyl auto-instrumentation.</summary>
    public const string Process = "PROCESS";
    /// <summary>Well-known Quartz value used by qyl auto-instrumentation.</summary>
    public const string Quartz = "QUARTZ";
    /// <summary>Well-known Rabbit Mq value used by qyl auto-instrumentation.</summary>
    public const string RabbitMq = "RABBITMQ";
    /// <summary>Well-known Sql Client value used by qyl auto-instrumentation.</summary>
    public const string SqlClient = "SQLCLIENT";
    /// <summary>Well-known Sqlite value used by qyl auto-instrumentation.</summary>
    public const string Sqlite = "SQLITE";
    /// <summary>Well-known Stack Exchange Redis value used by qyl auto-instrumentation.</summary>
    public const string StackExchangeRedis = "STACKEXCHANGEREDIS";
    /// <summary>Well-known Wcf Client value used by qyl auto-instrumentation.</summary>
    public const string WcfClient = "WCFCLIENT";
    /// <summary>Well-known Wcf Core value used by qyl auto-instrumentation.</summary>
    public const string WcfCore = "WCFCORE";
    /// <summary>Well-known Wcf Service value used by qyl auto-instrumentation.</summary>
    public const string WcfService = "WCFSERVICE";
    /// <summary>Well-known I Logger value used by qyl auto-instrumentation.</summary>
    public const string ILogger = "ILOGGER";
    /// <summary>Well-known Log4 Net value used by qyl auto-instrumentation.</summary>
    public const string Log4Net = "LOG4NET";
    /// <summary>Well-known N Log value used by qyl auto-instrumentation.</summary>
    public const string NLog = "NLOG";
}
