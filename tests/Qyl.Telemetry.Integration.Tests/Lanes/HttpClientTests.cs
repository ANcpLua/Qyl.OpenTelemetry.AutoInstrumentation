namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// One HttpClient.GetAsync produces exactly one non-Experimental span: the BCL's own client span. An
/// error status and a refused connection, managed and NativeAOT, with the BCL's duration metric; the
/// refused connection runs inside the application's own Spans.Work span, which carries the
/// RecordFailure triple.
/// </summary>
[TestClass]
[TestCategory("http-client")]
public sealed class HttpClientTests(TestContext testContext)
{
    private static readonly Dictionary<string, string[]> SpanSets = new(StringComparer.Ordinal)
    {
        ["error-status"] = ["System.Net.Http/Client"],
        // The refused connection and the application's Spans.Work span that records the failure.
        ["connection-failure"] = ["Qyl.Telemetry.Application/Internal", "System.Net.Http/Client"],
    };

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("http-client"), testContext);
        lane.BuildManaged();
        Verify(lane, (await lane.RunManagedAsync("managed HttpClient demo")));
        Verify(lane, (await lane.RunNativeAotAsync(lane.PublishNativeAot(), "NativeAOT HttpClient demo")));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("error-status=503", "expected-failure=HttpRequestException")
            .RequireReportPassed();
        LaneChecks.SpanSets(run, "SpanSets", "Operation", SpanSets);
        LaneChecks.ExcludedWithPrefix(run, "ExcludedScopes", "Experimental.", requireSome: false);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 3, "System.Net.Http and application activities"), "HttpClient");
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(run.RequireCount("Metrics", 2, "HttpClient duration metrics")));
    }
}
