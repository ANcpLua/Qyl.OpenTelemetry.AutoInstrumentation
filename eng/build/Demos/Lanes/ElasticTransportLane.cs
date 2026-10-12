namespace Qyl.Build;

/// <summary>Elastic.Transport's native source, sync and async, managed and NativeAOT.</summary>
sealed class ElasticTransportLane : DemoLane
{
    public override string Name => "elastictransport";

    public override string Demo => "Qyl.RealElasticTransportDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed Elastic.Transport demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT Elastic.Transport demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("elastictransport-sync=False", "elastictransport-async=False")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "Elastic.Transport activities"), "Elastic.Transport");
    }
}
