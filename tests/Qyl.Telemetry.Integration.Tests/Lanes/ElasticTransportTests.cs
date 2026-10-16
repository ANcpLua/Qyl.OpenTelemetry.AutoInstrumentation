namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>Elastic.Transport's native source, sync and async, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("elastictransport")]
public sealed class ElasticTransportTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("elastictransport"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed Elastic.Transport demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT Elastic.Transport demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("elastictransport-sync=False", "elastictransport-async=False")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "Elastic.Transport activities"), "Elastic.Transport");
    }
}
