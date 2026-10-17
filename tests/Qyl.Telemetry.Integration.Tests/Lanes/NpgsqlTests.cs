namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// Npgsql's native source against the testbed PostgreSQL, managed and NativeAOT: the physical connect
/// and every command, and nothing else — returning a connection to the pool and taking it back out
/// are untraced. The failing select runs inside the application's own Spans.Work span, which carries
/// the RecordFailure triple.
/// </summary>
[TestClass]
[TestCategory("npgsql")]
public sealed class NpgsqlTests(TestContext testContext)
{
    private static readonly Dictionary<string, string[]> Operations = new(StringComparer.Ordinal)
    {
        ["connection-open"] = ["Npgsql|Client"],
        ["select"] = ["Npgsql|Client"],
        // The failing command and the application's Spans.Work span that records the failure.
        ["failing-select"] = ["Npgsql|Client", "Qyl.Telemetry.Application|Internal"],
        ["connection-close"] = [],
        ["pooled-reopen"] = [],
    };

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("npgsql"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed Npgsql demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT Npgsql demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            // The SQLSTATE for undefined_column, which is also what Npgsql writes into error.type.
            .RequireTokens("connection-state=Open", "select-scalar=1", "expected-postgres-sqlstate=42703")
            .RequireReportPassed();
        LaneChecks.ExactStrings(run, "ExcludedSourcePrefixes", ["Experimental."]);
        LaneChecks.SpanSets(run, "Operations", "Operation", Operations);
        var total = Operations.Values.Sum(static spans => spans.Length);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", total, "Npgsql activities"), "Npgsql");
    }
}
