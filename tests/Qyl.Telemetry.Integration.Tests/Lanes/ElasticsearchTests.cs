namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>Elastic.Clients.Elasticsearch enriching the Elastic.Transport source, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("elasticsearch")]
public sealed class ElasticsearchTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("elasticsearch"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed Elasticsearch demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT Elasticsearch demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokenCount("expected-elasticsearch-error=TransportException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "Elasticsearch activities"), "Elasticsearch");
    }
}
