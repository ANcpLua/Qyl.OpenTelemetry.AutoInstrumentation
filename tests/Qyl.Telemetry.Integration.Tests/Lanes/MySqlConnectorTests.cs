namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// MySqlConnector's native source against the testbed MySQL, managed and NativeAOT. Unlike Npgsql it
/// traces every Open, pooled or not; closing the connection is what it does not trace.
/// </summary>
[TestClass]
[TestCategory("mysqlconnector")]
public sealed class MySqlConnectorTests(TestContext testContext)
{
    // MySqlConnector picks its attribute flavour from this variable, and the README documents qyl
    // against the stable one. The demo sets it too, so neither side can drift silently.
    private const string StabilityOptIn = "database";

    private static readonly Dictionary<string, string[]> s_operations = new(StringComparer.Ordinal)
    {
        ["connection-open"] = ["MySqlConnector|Client"],
        ["select"] = ["MySqlConnector|Client"],
        ["failing-select"] = ["MySqlConnector|Client"],
        ["connection-close"] = [],
        ["pooled-reopen"] = ["MySqlConnector|Client"],
    };

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("mysqlconnector"), testContext);
        var environment = lane.With(("OTEL_SEMCONV_STABILITY_OPT_IN", StabilityOptIn));
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed MySqlConnector demo", environment)));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT MySqlConnector demo", environment)));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            // ER_BAD_FIELD_ERROR, which the failed span carries in error.type and db.response.status_code.
            .RequireTokens("connection-state=Open", "select-scalar=1", "expected-mysql-error-number=1054")
            .RequireReportPassed();
        LaneChecks.ExactStrings(run, "ExcludedSourcePrefixes", ["Experimental."]);
        LaneChecks.SpanSets(run, "Operations", "Operation", s_operations);
        var total = s_operations.Values.Sum(static spans => spans.Length);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", total, "MySqlConnector activities"), "MySqlConnector");
    }
}
