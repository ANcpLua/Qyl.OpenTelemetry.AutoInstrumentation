namespace Qyl.Build;

/// <summary>
/// AddQyl() registers the OpenTelemetry ILogger provider, so an ILogger call becomes a LogRecord and
/// never a qyl Activity. The global logs control unset (six records exported) and false (provider
/// unregistered, nothing exported), managed and NativeAOT.
/// </summary>
sealed class ILoggerLane : DemoLane
{
    private const string LogsControl = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED";

    private static readonly (string Severity, string Body)[] s_records =
    [
        ("Trace", "qyl trace record"),
        ("Debug", "qyl debug record"),
        ("Information", "qyl information record"),
        ("Warning", "qyl warning record"),
        ("Error", "qyl error record"),
        ("Critical", "qyl critical record"),
    ];

    public override string Name => "ilogger";

    public override string Demo => "Qyl.RealILoggerDemo";

    public override bool RunsNativeAot => true;

    public override void Run(LaneContext lane)
    {
        var disabled = lane.With((LogsControl, "false"));
        lane.BuildManaged();
        var executable = lane.PublishNativeAot();
        Verify(lane, lane.RunManaged("managed ILogger demo"), logsEnabled: true);
        Verify(lane, lane.RunManaged($"managed ILogger demo ({LogsControl}=false)", disabled), logsEnabled: false);
        Verify(lane, lane.RunNativeAot(executable, "NativeAOT ILogger demo"), logsEnabled: true);
        Verify(lane, lane.RunNativeAot(executable, $"NativeAOT ILogger demo ({LogsControl}=false)", disabled), logsEnabled: false);
    }

    private static void Verify(LaneContext lane, DemoRun run, bool logsEnabled)
    {
        var expected = logsEnabled ? s_records : [];
        run.RequireSucceeded()
            .RequireTokens($"ilogger-record-count={expected.Length}")
            .RequireReportPassed()
            .RequireBool("LogsControlEnabled", logsEnabled)
            .RequireBool("OpenTelemetryLoggerProviderRegistered", logsEnabled);

        // The regression assertion for the deleted log-as-span lane.
        run.RequireCount("QylActivities", 0, "qyl activities for log calls");
        var records = run.RequireCount("Records", expected.Length, "exported log records");
        var actual = records.EnumerateArray().Select(static record => (ReportedActivity.Text(record, "Severity"), ReportedActivity.Text(record, "Body"))).ToArray();
        Gate.Require(actual.SequenceEqual(expected.Select(static record => ((string?)record.Severity, (string?)record.Body))),
            () => $"{run.Label} log record severity/body mismatch: {string.Join("; ", actual)}");
        lane.Telemetry.Logs(run, expected);
    }
}
