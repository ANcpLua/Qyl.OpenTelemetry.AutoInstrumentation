namespace Qyl.Build;

/// <summary>
/// Npgsql's native source against the testbed PostgreSQL, managed and NativeAOT: the physical connect
/// and every command, and nothing else — returning a connection to the pool and taking it back out
/// are untraced.
/// </summary>
sealed class NpgsqlLane : DemoLane
{
    private static readonly Dictionary<string, string[]> s_operations = new(StringComparer.Ordinal)
    {
        ["connection-open"] = ["Npgsql|Client"],
        ["select"] = ["Npgsql|Client"],
        ["failing-select"] = ["Npgsql|Client"],
        ["connection-close"] = [],
        ["pooled-reopen"] = [],
    };

    public override string Name => "npgsql";

    public override string Demo => "Qyl.RealNpgsqlDemo";

    public override bool RunsNativeAot => true;

    public override IReadOnlyList<TestbedService> Services => [TestbedService.Postgres];

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed Npgsql demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT Npgsql demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            // The SQLSTATE for undefined_column, which is also what Npgsql writes into error.type.
            .RequireTokens("connection-state=Open", "select-scalar=1", "expected-postgres-sqlstate=42703")
            .RequireReportPassed();
        LaneChecks.ExactStrings(run, "ExcludedSourcePrefixes", ["Experimental."]);
        LaneChecks.SpanSets(run, "Operations", "Operation", s_operations);
        var total = s_operations.Values.Sum(static spans => spans.Length);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", total, "Npgsql activities"), "Npgsql");
    }
}
