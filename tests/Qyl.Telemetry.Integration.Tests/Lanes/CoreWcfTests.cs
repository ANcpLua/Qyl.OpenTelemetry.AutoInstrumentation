namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>CoreWCF's server ActivitySource, managed only, with its registration on and toggled off.</summary>
[TestClass]
[TestCategory("corewcf")]
public sealed class CoreWcfTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("corewcf"), testContext);
        lane.BuildManaged("--disable-build-servers");
        Verify(lane, (await lane.RunManagedAsync("managed CoreWCF demo")), registrationEnabled: true);
        Verify(lane, (await lane.RunManagedAsync("managed CoreWCF demo (WCFCORE traces disabled)",
            lane.With(("OTEL_DOTNET_AUTO_TRACES_WCFCORE_INSTRUMENTATION_ENABLED", "false")))), registrationEnabled: false);
    }

    private static void Verify(DemoContext lane, DemoRun run, bool registrationEnabled)
    {
        run.RequireSucceeded().RequirePass()
            .RequireBool("QylRegistrationEnabled", registrationEnabled)
            .RequireBool("QylRegistrationObserved", registrationEnabled);
        lane.Telemetry.Spans(run, run.RequireCount("Activities", registrationEnabled ? 2 : 0, "CoreWCF activities"), "CoreWCF");
    }
}
