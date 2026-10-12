namespace Qyl.Build;

/// <summary>The Azure SDK's native Azure.* sources through a failing blob client, managed and NativeAOT.</summary>
sealed class AzureLane : DemoLane
{
    public override string Name => "azure";

    public override string Demo => "Qyl.RealAzureDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed Azure demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT Azure demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokenCount("expected-azure-error=RequestFailedException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "Azure activities"), "Azure");
    }
}
