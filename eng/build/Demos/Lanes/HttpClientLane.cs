namespace Qyl.Build;

/// <summary>
/// One HttpClient.GetAsync produces exactly one non-Experimental span: the BCL's own client span. An
/// error status and a refused connection, managed and NativeAOT, with the BCL's duration metric; the
/// refused connection runs inside the application's own Spans.Work span, which carries the
/// RecordFailure triple.
/// </summary>
sealed class HttpClientLane : DemoLane
{
    private static readonly Dictionary<string, string[]> s_spanSets = new(StringComparer.Ordinal)
    {
        ["error-status"] = ["System.Net.Http/Client"],
        // The refused connection and the application's Spans.Work span that records the failure.
        ["connection-failure"] = ["Qyl.Telemetry.Application/Internal", "System.Net.Http/Client"],
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
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 3, "System.Net.Http and application activities"), "HttpClient");
        lane.Telemetry.Metrics(run, LaneChecks.Metrics(run.RequireCount("Metrics", 2, "HttpClient duration metrics")));
    }
}
