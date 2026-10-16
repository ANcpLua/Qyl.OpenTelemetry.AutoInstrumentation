namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>The WCF ClientBase interceptor against an unreachable endpoint, managed only.</summary>
[TestClass]
[TestCategory("wcf-client")]
public sealed class WcfClientTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("wcf-client"), testContext);
        lane.BuildManaged();
        var run = (await lane.RunManagedAsync("managed WCF client demo"))
            .RequireSucceeded()
            .RequireTokenCount("expected-wcf-error=CommunicationException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "WCF client activities"), "WCF client");
    }
}
