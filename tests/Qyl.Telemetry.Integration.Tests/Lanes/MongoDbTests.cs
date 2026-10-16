namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// MongoDB.Driver's native source against the testbed MongoDB, managed: three collection commands,
/// each traced in two layers (an operation span and the command span beneath it). The driver's
/// handshake and topology commands name no collection and are filtered out by the demo.
/// </summary>
[TestClass]
[TestCategory("mongodb")]
public sealed class MongoDbTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("mongodb"), testContext);
        lane.BuildManaged();
        var run = (await lane.RunManagedAsync("managed MongoDB demo"))
            .RequireSucceeded()
            .RequireTokens("inserted-documents=1", "found-documents=1", "deleted-documents=1")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 6, "MongoDB collection activities"), "MongoDB");
    }
}
