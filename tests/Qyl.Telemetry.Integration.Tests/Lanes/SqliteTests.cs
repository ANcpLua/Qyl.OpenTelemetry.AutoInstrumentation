namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The ADO.NET interceptor's SQLITE fan-out against Microsoft.Data.Sqlite, managed and NativeAOT.</summary>
[TestClass]
[TestCategory("sqlite")]
public sealed class SqliteTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("sqlite"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed SQLite demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT SQLite demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("sqlite-value=alpha", "expected-sqlite-error=1").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "SQLite activities"), "SQLite");
    }
}
