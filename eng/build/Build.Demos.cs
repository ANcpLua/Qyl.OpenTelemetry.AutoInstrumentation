namespace Qyl.Build;

sealed partial class QylBuild
{
    [Parameter("Demos: the lanes to run, by name (default: all thirty) — e.g. --lane quartz http-client")]
    readonly string[] Lane = [];

    Target Demos => definition => definition
        .Description("Native MSTest integration cases: real managed/NativeAOT applications, independent reports and complete OTLP captures")
        .After(AotPublishGate)
        .Executes(() =>
        {
            var lanes = Lane.Length == 0 ? DemoLanes.All : Lane.Select(DemoLanes.ByName).ToArray();
            var results = Repo.Artifacts / "integration-results" / Guid.NewGuid().ToString("N");
            results.CreateDirectory();
            var filter = Lane.Length == 0 ? Array.Empty<string>() : new[] { "--filter", string.Join('|', lanes.Select(static lane => "TestCategory=" + lane.Name)) };
            // Full discovery includes four representation cases and three transport/configuration
            // cases as well as the lanes. Missing those public-pipeline checks must fail the run.
            var minimum = Lane.Length == 0 ? lanes.Count + 7 : lanes.Count;
            Shell.Checked("dotnet", ["run", "--project", Repo.IntegrationTests, "-c", "Release", "--",
                "--report-trx", "--results-directory", results, "--minimum-expected-tests", minimum.ToString(System.Globalization.CultureInfo.InvariantCulture), .. filter],
                Repo.Root, timeout: TimeSpan.FromHours(3), echo: true);
            Log.Information("demos-ok lanes={Lanes}", string.Join(",", lanes.Select(static lane => lane.Name)));
        });
}
