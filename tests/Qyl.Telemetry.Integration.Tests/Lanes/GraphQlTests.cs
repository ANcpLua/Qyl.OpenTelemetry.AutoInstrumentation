namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// GraphQL.NET's ActivitySource is silent until the application calls UseTelemetry() itself: the
/// opted-in executer produces exactly one span, the identical executer without the opt-in none.
/// Managed and NativeAOT (a vendor-warned publish).
/// </summary>
[TestClass]
[TestCategory("graphql")]
public sealed class GraphQlTests(TestContext testContext)
{
    private static readonly Dictionary<string, string[]> s_spanSets = new(StringComparer.Ordinal)
    {
        ["query-with-telemetry"] = ["GraphQL/Internal"],
        ["query-without-telemetry"] = [],
    };

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("graphql"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed GraphQL demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT GraphQL demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("graphql-with-telemetry-success=true", "graphql-without-telemetry-success=true")
            .RequireReportPassed();
        LaneChecks.SpanSets(run, "SpanSets", "Operation", s_spanSets);
        LaneChecks.ExcludedWithPrefix(run, "ExcludedScopes", "Experimental.", requireSome: false);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 1, "GraphQL activity"), "GraphQL");
    }
}
