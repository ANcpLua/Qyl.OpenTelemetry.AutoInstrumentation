namespace Qyl.Build;

/// <summary>
/// One demo application and everything its gate asserts. A lane builds the demo (and publishes it
/// NativeAOT where the integration supports it), runs it with OTLP pointed at the local qyl collector,
/// holds the demo's own report to the expectations restated here, and checks through the collector API
/// that what the report describes is what arrived over OTLP.
/// </summary>
abstract class DemoLane
{
    /// <summary>The <c>--lane</c> name.</summary>
    public abstract string Name { get; }

    /// <summary>The demo project under demos/.</summary>
    public abstract string Demo { get; }

    /// <summary>Whether the lane executes the demo's NativeAOT binary.</summary>
    public abstract bool RunsNativeAot { get; }

    /// <summary>The Railway testbed services the demo connects to; empty for a self-contained demo.</summary>
    public virtual IReadOnlyList<TestbedService> Services => [];

    /// <summary>The <c>service.name</c> the demo exports under.</summary>
    public virtual string ServiceName => $"qyl-real-{Name}-demo";

    /// <summary>The lane's source file, which the contract cites as its runtime evidence.</summary>
    public string EvidencePath => $"eng/build/Demos/Lanes/{GetType().Name}.cs";

    public AbsolutePath Project => Repo.DemoProject(Demo);

    public abstract void Run(LaneContext lane);
}

static class DemoLanes
{
    public static readonly IReadOnlyList<DemoLane> All =
    [
        new AdoNetLane(),
        new AspNetCoreLane(),
        new AspNetCoreMetricsLane(),
        new AzureLane(),
        new CoreWcfLane(),
        new EfCoreLane(),
        new ElasticsearchLane(),
        new ElasticTransportLane(),
        new GenAiLane(),
        new GraphQlLane(),
        new GrpcClientLane(),
        new HttpClientLane(),
        new ILoggerLane(),
        new KafkaLane(),
        new MassTransitLane(),
        new McpLane(),
        new MongoDbLane(),
        new MySqlConnectorLane(),
        new MySqlDataLane(),
        new NetRuntimeMetricsLane(),
        new NpgsqlLane(),
        new NServiceBusLane(),
        new OracleMdaLane(),
        new QuartzLane(),
        new RabbitMqLane(),
        new RedisLane(),
        new SessionPropagationLane(),
        new SqlClientLane(),
        new SqliteLane(),
        new WcfClientLane(),
    ];

    public static DemoLane? ByEvidencePath(string path) =>
        All.FirstOrDefault(lane => string.Equals(lane.EvidencePath, path, StringComparison.Ordinal));

    public static DemoLane ByName(string name) =>
        All.FirstOrDefault(lane => string.Equals(lane.Name, name, StringComparison.Ordinal))
        ?? throw Gate.Fail($"unknown lane {name}; lanes: {string.Join(", ", All.Select(static lane => lane.Name))}");
}
