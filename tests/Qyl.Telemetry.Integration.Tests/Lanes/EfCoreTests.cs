namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>EF Core's DiagnosticListener lane against SQLite, managed and NativeAOT (compiled model,
/// a vendor-warned publish), with and without the statement-text opt-in.</summary>
[TestClass]
[TestCategory("efcore")]
public sealed class EfCoreTests(TestContext testContext)
{
    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("efcore"), testContext);
        lane.BuildManaged();
        var executable = lane.PublishNativeAot();
        Verify(lane, (await lane.RunManagedAsync("managed EFCore demo")));
        Verify(lane, (await lane.RunNativeAotAsync(executable, "NativeAOT EFCore demo")));

        // The demo requires db.query.text on its spans when the opt-in is set, and absent above.
        var optIn = lane.With(("OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT", "true"));
        Verify(lane, (await lane.RunManagedAsync("managed EFCore demo (statement opt-in)", optIn)));
        Verify(lane, (await lane.RunNativeAotAsync(executable, "NativeAOT EFCore demo (statement opt-in)", optIn)));
    }

    private static void Verify(DemoContext lane, DemoRun run)
    {
        run.RequireSucceeded().RequireTokens("expected-failure=SqliteException").RequireReportPassed();
        lane.Telemetry.Spans(run, run.RequireCount("Activities", 3, "EFCore activities"), "EFCore");
    }
}
