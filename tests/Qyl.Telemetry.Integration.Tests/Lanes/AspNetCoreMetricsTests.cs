namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The ASP.NET Core component meters, managed and NativeAOT (a vendor-warned publish).</summary>
[TestClass]
[TestCategory("aspnetcore-metrics")]
public sealed class AspNetCoreMetricsTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("aspnetcore-metrics"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed ASP.NET Core metrics demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT ASP.NET Core metrics demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireReportPassed();
        var metrics = run.RequireAtLeast("Metrics", 3, "ASP.NET Core component metrics");
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(metrics));
    }
}
