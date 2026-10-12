namespace Qyl.Build;

/// <summary>CoreWCF's server ActivitySource, managed only, with its registration on and toggled off.</summary>
sealed class CoreWcfLane : DemoLane
{
    public override string Name => "corewcf";

    public override string Demo => "Qyl.RealCoreWcfDemo";

    public override bool RunsNativeAot => false;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged("--disable-build-servers");
        Verify(lane, lane.RunManaged("managed CoreWCF demo"), registrationEnabled: true);
        Verify(lane, lane.RunManaged("managed CoreWCF demo (WCFCORE traces disabled)",
            lane.With(("OTEL_DOTNET_AUTO_TRACES_WCFCORE_INSTRUMENTATION_ENABLED", "false"))), registrationEnabled: false);
    }

    private static void Verify(LaneContext lane, DemoRun run, bool registrationEnabled)
    {
        run.RequireSucceeded().RequirePass()
            .RequireBool("QylRegistrationEnabled", registrationEnabled)
            .RequireBool("QylRegistrationObserved", registrationEnabled);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", registrationEnabled ? 2 : 0, "CoreWCF activities"), "CoreWCF");
    }
}
