namespace Qyl.Build;

/// <summary>The ASP.NET Core component meters, managed and NativeAOT (a vendor-warned publish).</summary>
sealed class AspNetCoreMetricsLane : DemoLane
{
    public override string Name => "aspnetcore-metrics";

    public override string Demo => "Qyl.RealAspNetCoreMetricsDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed ASP.NET Core metrics demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT ASP.NET Core metrics demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireReportPassed();
        var metrics = run.RequireAtLeast("Metrics", 3, "ASP.NET Core component metrics");
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(metrics));
    }
}
