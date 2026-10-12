namespace Qyl.Build;

/// <summary>
/// GraphQL.NET's ActivitySource is silent until the application calls UseTelemetry() itself: the
/// opted-in executer produces exactly one span, the identical executer without the opt-in none.
/// Managed and NativeAOT (a vendor-warned publish).
/// </summary>
sealed class GraphQlLane : DemoLane
{
    private static readonly Dictionary<string, string[]> s_spanSets = new(StringComparer.Ordinal)
    {
        ["query-with-telemetry"] = ["GraphQL/Internal"],
        ["query-without-telemetry"] = [],
    };

    public override string Name => "graphql";

    public override string Demo => "Qyl.RealGraphQlDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed GraphQL demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT GraphQL demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("graphql-with-telemetry-success=true", "graphql-without-telemetry-success=true")
            .RequireReportPassed();
        LaneChecks.SpanSets(run, "SpanSets", "Operation", s_spanSets);
        LaneChecks.ExcludedWithPrefix(run, "ExcludedScopes", "Experimental.", requireSome: false);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 1, "GraphQL activity"), "GraphQL");
    }
}
