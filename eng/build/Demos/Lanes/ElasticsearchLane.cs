namespace Qyl.Build;

/// <summary>Elastic.Clients.Elasticsearch enriching the Elastic.Transport source, managed and NativeAOT.</summary>
sealed class ElasticsearchLane : DemoLane
{
    public override string Name => "elasticsearch";

    public override string Demo => "Qyl.RealElasticsearchDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed Elasticsearch demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT Elasticsearch demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded()
            .RequireTokenCount("expected-elasticsearch-error=TransportException", 2)
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "Elasticsearch activities"), "Elasticsearch");
    }
}
