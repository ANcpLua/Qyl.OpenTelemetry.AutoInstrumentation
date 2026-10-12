namespace Qyl.Build;

/// <summary>The WCF ClientBase interceptor against an unreachable endpoint, managed only.</summary>
sealed class WcfClientLane : DemoLane
{
    public override string Name => "wcf-client";

    public override string Demo => "Qyl.RealWcfClientDemo";

    public override bool RunsNativeAot => false;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var run = lane.RunManaged("managed WCF client demo")
            .RequireSucceeded()
            .RequireTokenCount("expected-wcf-error=CommunicationException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "WCF client activities"), "WCF client");
    }
}
