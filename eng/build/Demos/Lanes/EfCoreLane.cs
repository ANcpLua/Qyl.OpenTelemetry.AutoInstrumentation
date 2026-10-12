namespace Qyl.Build;

/// <summary>EF Core's DiagnosticListener lane against SQLite, managed and NativeAOT (compiled model,
/// a vendor-warned publish), with and without the statement-text opt-in.</summary>
sealed class EfCoreLane : DemoLane
{
    public override string Name => "efcore";

    public override string Demo => "Qyl.RealEfCoreDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var executable = lane.PublishNativeAot();
        Verify(lane, lane.RunManaged("managed EFCore demo"));
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT EFCore demo"));

        // The demo requires db.query.text on its spans when the opt-in is set, and absent above.
        var optIn = lane.With(("OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT", "true"));
        Verify(lane, lane.RunManaged("managed EFCore demo (statement opt-in)", optIn));
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT EFCore demo (statement opt-in)", optIn));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("expected-failure=SqliteException").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 3, "EFCore activities"), "EFCore");
    }
}
