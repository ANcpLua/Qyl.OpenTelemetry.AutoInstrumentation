namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>RabbitMQ.Client's publisher source against the testbed RabbitMQ, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("rabbitmq")]
public sealed class RabbitMqTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("rabbitmq"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed RabbitMQ demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT RabbitMQ demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("published-queue=", "published-exchange=qyl-probe-exchange").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "RabbitMQ activities"), "RabbitMQ");
    }
}
