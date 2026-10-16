namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The generic ADO.NET DbCommand interceptor against SQLite, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("adonet")]
public sealed class AdoNetTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("adonet"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed ADO.NET demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT ADO.NET demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("adonet-value=alpha", "expected-adonet-error=SqliteException")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "ADO.NET activities"), "ADO.NET");
    }
}
