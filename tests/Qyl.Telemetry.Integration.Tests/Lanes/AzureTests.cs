namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The Azure SDK's native Azure.* sources through a failing blob client, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("azure")]
public sealed class AzureTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("azure"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed Azure demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT Azure demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokenCount("expected-azure-error=RequestFailedException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "Azure activities"), "Azure");
    }
}
