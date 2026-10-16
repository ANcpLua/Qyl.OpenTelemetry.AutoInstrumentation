namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>Confluent.Kafka's producer and consumer interceptors against the testbed Kafka (SASL/PLAIN), managed.</summary>
[TestClass]
[TestCategory("kafka")]
public sealed class KafkaTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("kafka"), testContext);
        lane.BuildManaged();
        var run = (await lane.RunManagedAsync("managed Kafka demo"))
            .RequireSucceeded()
            .RequireTokens("produced-offset=", "consumed-messages=2", "expected-kafka-error=")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireAtLeast("Activities", 5, "Kafka activities"), "Kafka");
    }
}
