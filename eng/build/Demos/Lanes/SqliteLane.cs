namespace Qyl.Build;

/// <summary>The ADO.NET interceptor's SQLITE fan-out against Microsoft.Data.Sqlite, managed and NativeAOT.</summary>
sealed class SqliteLane : DemoLane
{
    public override string Name => "sqlite";

    public override string Demo => "Qyl.RealSqliteDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed SQLite demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT SQLite demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("sqlite-value=alpha", "expected-sqlite-error=1").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 4, "SQLite activities"), "SQLite");
    }
}
