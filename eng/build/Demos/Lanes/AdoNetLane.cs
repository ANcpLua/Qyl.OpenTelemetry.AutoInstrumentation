namespace Qyl.Build;

/// <summary>The generic ADO.NET DbCommand interceptor against SQLite, managed and NativeAOT.</summary>
sealed class AdoNetLane : DemoLane
{
    public override string Name => "adonet";

    public override string Demo => "Qyl.RealAdoNetDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed ADO.NET demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT ADO.NET demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("adonet-value=alpha", "expected-adonet-error=SqliteException")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "ADO.NET activities"), "ADO.NET");
    }
}
