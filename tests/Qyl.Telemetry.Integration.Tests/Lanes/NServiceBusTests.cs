namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// NServiceBus traces its own pipeline: the publish, the routed send, the send that cannot be routed,
/// the incoming "process message" span and the handler invocation beneath it. Managed only.
/// </summary>
[TestClass]
[TestCategory("nservicebus")]
public sealed class NServiceBusTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("nservicebus"), testContext);
        lane.BuildManaged();
        var run = (await lane.RunManagedAsync("managed NServiceBus demo"))
            .RequireSucceeded()
            .RequireTokens("published=alpha", "sent-and-handled=beta", "expected-nservicebus-error=no-route")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 5, "NServiceBus activities"), "NServiceBus");
    }
}
