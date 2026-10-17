namespace Qyl.Build.Demos;

/// <summary>Application metadata shared by native integration tests and the independent Weaver run.</summary>
sealed record DemoDefinition(string Name, string Demo, bool RunsNativeAot, IReadOnlyList<TestbedService> Services, string? ServiceNameOverride, string TestClass)
{
    public string ServiceName => ServiceNameOverride ?? $"qyl-real-{Name}-demo";
    public string EvidencePath => $"tests/Qyl.Telemetry.Integration.Tests/Lanes/{TestClass}.cs";
    public AbsolutePath Project => Repo.DemoProject(Demo);
}

static class DemoLanes
{
    public static readonly IReadOnlyList<DemoDefinition> All =
    [
        new("adonet", "Qyl.RealAdoNetDemo", true, [], null, "AdoNetTests"),
        new("aspnetcore", "Qyl.RealAspNetCoreDemo", true, [], null, "AspNetCoreTests"),
        new("aspnetcore-metrics", "Qyl.RealAspNetCoreMetricsDemo", true, [], null, "AspNetCoreMetricsTests"),
        new("azure", "Qyl.RealAzureDemo", true, [], null, "AzureTests"),
        new("corewcf", "Qyl.RealCoreWcfDemo", false, [], null, "CoreWcfTests"),
        new("efcore", "Qyl.RealEfCoreDemo", true, [], null, "EfCoreTests"),
        new("elastictransport", "Qyl.RealElasticTransportDemo", true, [], null, "ElasticTransportTests"),
        new("elasticsearch", "Qyl.RealElasticsearchDemo", true, [], null, "ElasticsearchTests"),
        new("genai", "Qyl.RealGenAiDemo", false, [], null, "GenAiTests"),
        new("graphql", "Qyl.RealGraphQlDemo", true, [], null, "GraphQlTests"),
        new("grpc-client", "Qyl.RealGrpcClientDemo", true, [], null, "GrpcClientTests"),
        new("http-client", "Qyl.RealHttpClientDemo", true, [], null, "HttpClientTests"),
        new("ilogger", "Qyl.RealILoggerDemo", true, [], null, "LoggerTests"),
        new("kafka", "Qyl.RealKafkaDemo", false, [TestbedService.Kafka], null, "KafkaTests"),
        new("masstransit", "Qyl.RealMassTransitDemo", true, [TestbedService.RabbitMq], null, "MassTransitTests"),
        new("mcp", "Qyl.RealMcpDemo", true, [], null, "McpTests"),
        new("mongodb", "Qyl.RealMongoDbDemo", false, [TestbedService.MongoDb], null, "MongoDbTests"),
        new("mysqlconnector", "Qyl.RealMySqlConnectorDemo", true, [TestbedService.MySql], null, "MySqlConnectorTests"),
        new("mysqldata", "Qyl.RealMySqlDataDemo", true, [TestbedService.MySql], null, "MySqlDataTests"),
        new("nservicebus", "Qyl.RealNServiceBusDemo", false, [], null, "NServiceBusTests"),
        new("netruntime-metrics", "Qyl.RealNetRuntimeMetricsDemo", true, [], null, "NetRuntimeMetricsTests"),
        new("npgsql", "Qyl.RealNpgsqlDemo", true, [TestbedService.Postgres], null, "NpgsqlTests"),
        new("oraclemda", "Qyl.RealOracleMdaDemo", true, [TestbedService.Oracle], null, "OracleMdaTests"),
        new("quartz", "Qyl.RealQuartzDemo", false, [], null, "QuartzTests"),
        new("rabbitmq", "Qyl.RealRabbitMqDemo", true, [TestbedService.RabbitMq], null, "RabbitMqTests"),
        new("redis", "Qyl.RealRedisDemo", true, [TestbedService.Redis], null, "RedisTests"),
        new("session-propagation", "Qyl.RealSessionPropagationDemo", false, [], "qyl-real-session-propagation-demo-upstream", "SessionPropagationTests"),
        new("sqlclient", "Qyl.RealSqlClientDemo", true, [TestbedService.SqlServer], null, "SqlClientTests"),
        new("sqlite", "Qyl.RealSqliteDemo", true, [], null, "SqliteTests"),
        new("wcf-client", "Qyl.RealWcfClientDemo", false, [], null, "WcfClientTests"),
    ];

    public static DemoDefinition? ByEvidencePath(string path) =>
        All.FirstOrDefault(lane => string.Equals(lane.EvidencePath, path, StringComparison.Ordinal));

    public static DemoDefinition ByName(string name) =>
        All.FirstOrDefault(lane => string.Equals(lane.Name, name, StringComparison.Ordinal))
        ?? throw Gate.Fail($"unknown lane {name}; lanes: {string.Join(", ", All.Select(static lane => lane.Name))}");
}
