namespace Qyl.Build;

/// <summary>
/// NServiceBus traces its own pipeline: the publish, the routed send, the send that cannot be routed,
/// the incoming "process message" span and the handler invocation beneath it. Managed only.
/// </summary>
sealed class NServiceBusLane : DemoLane
{
    public override string Name => "nservicebus";

    public override string Demo => "Qyl.RealNServiceBusDemo";

    public override bool RunsNativeAot => false;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var run = lane.RunManaged("managed NServiceBus demo")
            .RequireSucceeded()
            .RequireTokens("published=alpha", "sent-and-handled=beta", "expected-nservicebus-error=no-route")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 5, "NServiceBus activities"), "NServiceBus");
    }
}
