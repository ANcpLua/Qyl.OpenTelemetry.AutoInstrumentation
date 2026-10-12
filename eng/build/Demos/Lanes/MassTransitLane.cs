namespace Qyl.Build;

/// <summary>
/// MassTransit's own ActivitySource over the testbed RabbitMQ: one native span per command, the
/// publish and the explicit send. A third would be the deleted interceptor emitting alongside it.
/// Managed and NativeAOT (a vendor-warned publish).
/// </summary>
sealed class MassTransitLane : DemoLane
{
    public override string Name => "masstransit";

    public override string Demo => "Qyl.RealMassTransitDemo";

    public override bool RunsNativeAot => true;

    public override IReadOnlyList<TestbedService> Services => [TestbedService.RabbitMq];

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        Verify(lane, lane.RunManaged("managed MassTransit demo"));
        Verify(lane, lane.RunNativeAot(lane.PublishNativeAot(), "NativeAOT MassTransit demo"));
    }

    private static void Verify(LaneContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("published=alpha", "sent=beta").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "MassTransit activities"), "MassTransit");
    }
}
