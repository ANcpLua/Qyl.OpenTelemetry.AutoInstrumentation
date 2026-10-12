namespace Qyl.Build;

/// <summary>
/// MongoDB.Driver's native source against the testbed MongoDB, managed: three collection commands,
/// each traced in two layers (an operation span and the command span beneath it). The driver's
/// handshake and topology commands name no collection and are filtered out by the demo.
/// </summary>
sealed class MongoDbLane : DemoLane
{
    public override string Name => "mongodb";

    public override string Demo => "Qyl.RealMongoDbDemo";

    public override bool RunsNativeAot => false;

    public override IReadOnlyList<TestbedService> Services => [TestbedService.MongoDb];

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        var run = lane.RunManaged("managed MongoDB demo")
            .RequireSucceeded()
            .RequireTokens("inserted-documents=1", "found-documents=1", "deleted-documents=1")
            .RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 6, "MongoDB collection activities"), "MongoDB");
    }
}
