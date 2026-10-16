namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The System.Runtime meter and the process instruments, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("netruntime-metrics")]
public sealed class NetRuntimeMetricsTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("netruntime-metrics"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed .NET runtime metrics demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT .NET runtime metrics demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireReportPassed();
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(run.RequireAtLeast("Metrics", 6, ".NET runtime/process metrics")));
    }
}
