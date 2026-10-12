namespace Qyl.Build;

/// <summary>RabbitMQ.Client's publisher source against the testbed RabbitMQ, managed and NativeAOT.</summary>
sealed class RabbitMqLane : DemoLane
{
    public override string Name => "rabbitmq";

    public override string Demo => "Qyl.RealRabbitMqDemo";

    public override bool RunsNativeAot => true;

    public override IReadOnlyList<TestbedService> Services => [TestbedService.RabbitMq];

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed RabbitMQ demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT RabbitMQ demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("published-queue=", "published-exchange=qyl-probe-exchange").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "RabbitMQ activities"), "RabbitMQ");
    }
}
