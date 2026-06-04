namespace Qyl.AutoInstrumentation;

public enum QylAutoInstrumentationSignal
{
    Traces,
    Metrics,
    Logs,
}

public static class QylAutoInstrumentationIds
{
    public const string AdoNet = "ADONET";
    public const string AspNet = "ASPNET";
    public const string AspNetCore = "ASPNETCORE";
    public const string Azure = "AZURE";
    public const string Elasticsearch = "ELASTICSEARCH";
    public const string ElasticTransport = "ELASTICTRANSPORT";
    public const string EntityFrameworkCore = "ENTITYFRAMEWORKCORE";
    public const string GraphQl = "GRAPHQL";
    public const string GrpcNetClient = "GRPCNETCLIENT";
    public const string HttpClient = "HTTPCLIENT";
    public const string Kafka = "KAFKA";
    public const string MassTransit = "MASSTRANSIT";
    public const string MongoDb = "MONGODB";
    public const string MySqlConnector = "MYSQLCONNECTOR";
    public const string MySqlData = "MYSQLDATA";
    public const string NetRuntime = "NETRUNTIME";
    public const string Npgsql = "NPGSQL";
    public const string NServiceBus = "NSERVICEBUS";
    public const string OracleMda = "ORACLEMDA";
    public const string Process = "PROCESS";
    public const string Quartz = "QUARTZ";
    public const string RabbitMq = "RABBITMQ";
    public const string SqlClient = "SQLCLIENT";
    public const string Sqlite = "SQLITE";
    public const string StackExchangeRedis = "STACKEXCHANGEREDIS";
    public const string WcfClient = "WCFCLIENT";
    public const string WcfCore = "WCFCORE";
    public const string WcfService = "WCFSERVICE";
    public const string ILogger = "ILOGGER";
    public const string Log4Net = "LOG4NET";
    public const string NLog = "NLOG";
}
