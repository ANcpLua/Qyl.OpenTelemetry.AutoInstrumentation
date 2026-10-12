namespace Qyl.Build;

/// <summary>The System.Runtime meter and the process instruments, managed and NativeAOT.</summary>
sealed class NetRuntimeMetricsLane : DemoLane
{
    public override string Name => "netruntime-metrics";

    public override string Demo => "Qyl.RealNetRuntimeMetricsDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed .NET runtime metrics demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT .NET runtime metrics demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireReportPassed();
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(run.RequireAtLeast("Metrics", 6, ".NET runtime/process metrics")));
    }
}
