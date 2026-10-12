namespace Qyl.Build;

/// <summary>
/// One HttpClient.GetAsync produces exactly one non-Experimental span: the BCL's own client span. An
/// error status and a refused connection, managed and NativeAOT, with the BCL's duration metric.
/// </summary>
sealed class HttpClientLane : DemoLane
{
    private static readonly Dictionary<string, string[]> s_spanSets = new(StringComparer.Ordinal)
    {
        ["error-status"] = ["System.Net.Http/Client"],
        ["connection-failure"] = ["System.Net.Http/Client"],
    };

    public override string Name => "http-client";

    public override string Demo => "Qyl.RealHttpClientDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed HttpClient demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT HttpClient demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokens("error-status=503", "expected-failure=HttpRequestException")
            .RequireReportPassed();
        LaneChecks.SpanSets(run, "SpanSets", "Operation", s_spanSets);
        LaneChecks.ExcludedWithPrefix(run, "ExcludedScopes", "Experimental.", requireSome: false);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "System.Net.Http activities"), "System.Net.Http");
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(run.RequireCount("Metrics", 2, "HttpClient duration metrics")));
    }
}
