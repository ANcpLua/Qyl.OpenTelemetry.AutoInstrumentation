namespace Qyl.Telemetry.Integration.Tests.Lanes;

/// <summary>
/// AddQyl() registers the OpenTelemetry ILogger provider, so an ILogger call becomes a LogRecord and
/// never a qyl Activity. The global logs control unset (six records exported) and false (provider
/// unregistered, nothing exported), managed and NativeAOT.
/// </summary>
[TestClass]
[TestCategory("ilogger")]
public sealed class LoggerTests(TestContext testContext)
{
    private const string LogsControl = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED";

    private static readonly (string Severity, string Body)[] Records =
    [
        ("Trace", "qyl trace record"),
        ("Debug", "qyl debug record"),
        ("Information", "qyl information record"),
        ("Warning", "qyl warning record"),
        ("Error", "qyl error record"),
        ("Critical", "qyl critical record"),
    ];

    [TestMethod]
    public async Task ExportsExpectedTelemetry()
    {
        var lane = new DemoContext(DemoLanes.ByName("ilogger"), testContext);
        // Six records stay below the batch limit. Delay periodic export so the first HTTP/protobuf
        // request must be made by provider disposal, covering the SDK 1.20.0 shutdown fix.
        var enabled = lane.With(("OTEL_BLRP_SCHEDULE_DELAY", "3600000"));
        var disabled = lane.With((LogsControl, "false"), ("OTEL_BLRP_SCHEDULE_DELAY", "3600000"));
        lane.BuildManaged();
        var executable = lane.PublishNativeAot();
        Verify(lane, (await lane.RunManagedAsync("managed ILogger shutdown export", enabled)), logsEnabled: true);
        Verify(lane, (await lane.RunManagedAsync($"managed ILogger demo ({LogsControl}=false)", disabled)), logsEnabled: false);
        Verify(lane, (await lane.RunNativeAotAsync(executable, "NativeAOT ILogger shutdown export", enabled)), logsEnabled: true);
        Verify(lane, (await lane.RunNativeAotAsync(executable, $"NativeAOT ILogger demo ({LogsControl}=false)", disabled)), logsEnabled: false);
    }

    private static void Verify(DemoContext lane, DemoRun run, bool logsEnabled)
    {
        var expected = logsEnabled ? Records : [];
        run.RequireSucceeded()
            .RequireTokens($"ilogger-record-count={expected.Length}")
            .RequireReportPassed()
            .RequireBool("LogsControlEnabled", logsEnabled)
            .RequireBool("OpenTelemetryLoggerProviderRegistered", logsEnabled);

        // The regression assertion for the deleted log-as-span lane.
        run.RequireCount("QylActivities", 0, "qyl activities for log calls");
        var records = run.RequireCount("Records", expected.Length, "exported log records");
        var actual = records.EnumerateArray().Select(static record => (ReportedActivity.Text(record, "Severity"), ReportedActivity.Text(record, "Body"))).ToArray();
        Assert.IsTrue(actual.SequenceEqual(expected.Select(static record => ((string?)record.Severity, (string?)record.Body))), $"{run.Label} log record severity/body mismatch: {string.Join("; ", actual)}");
        lane.Telemetry.Logs(run, expected);
    }
}
