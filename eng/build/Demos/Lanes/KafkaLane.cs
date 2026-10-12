namespace Qyl.Build;

/// <summary>Confluent.Kafka's producer and consumer interceptors against the testbed Kafka (SASL/PLAIN), managed.</summary>
sealed class KafkaLane : DemoLane
{
    public override string Name => "kafka";

    public override string Demo => "Qyl.RealKafkaDemo";

    public override bool RunsNativeAot => false;

    public override IReadOnlyList<TestbedService> Services => [TestbedService.Kafka];

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var run = lane.RunManaged("managed Kafka demo")
            .RequireSucceeded()
            .RequireTokens("produced-offset=", "consumed-messages=2", "expected-kafka-error=")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireAtLeast("Activities", 5, "Kafka activities"), "Kafka");
    }
}
