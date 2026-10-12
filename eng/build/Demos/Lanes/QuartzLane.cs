namespace Qyl.Build;

/// <summary>
/// Quartz's native source and its native Meter, managed: one Quartz.Job.Execute span per scheduled job,
/// and the job duration series switched by the Quartz metrics toggle, the global metrics toggle and the
/// per-instrumentation override.
/// </summary>
sealed class QuartzLane : DemoLane
{
    private const string JobDuration = "quartz.job.execution.duration";

    public override string Name => "quartz";

    public override string Demo => "Qyl.RealQuartzDemo";

    public override bool RunsNativeAot => false;

    public override void Run(LaneContext lane)
    {
        lane.BuildManaged();
        (string Name, (string, string)[] Overrides, bool MetricsEnabled)[] cases =
        [
            ("default", [], true),
            ("Quartz metrics disabled", [("OTEL_DOTNET_AUTO_METRICS_QUARTZ_INSTRUMENTATION_ENABLED", "false")], false),
            ("all metrics disabled", [("OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED", "false")], false),
            ("Quartz metrics override", [("OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED", "false"), ("OTEL_DOTNET_AUTO_METRICS_QUARTZ_INSTRUMENTATION_ENABLED", "true")], true),
        ];
        foreach (var (name, overrides, metricsEnabled) in cases)
        {
            var run = lane.RunManaged($"managed Quartz demo ({name})", lane.With(overrides), metricsEnabled ? [] : ["--expect-no-quartz-metrics"]);
            run.RequireSucceeded().RequireTokens("scheduler-fired=true").RequireReportPassed();

            // One Quartz.Job.Execute span per scheduled job; the job-store calls on the same source are
            // not reported.
            lane.Telemetry.Spans(run, run.RequireCount("Activities", 2, "Quartz job-execute activities"), "Quartz");
            run.RequireBool("ExpectedMetricsEnabled", metricsEnabled);
            run.RequireCount("JobMetrics", metricsEnabled ? 2 : 0, "job duration series");
            if (metricsEnabled)
                lane.Telemetry.Metrics(run, [ReportedMetric.Named(JobDuration)]);
            else
                lane.Telemetry.NoMetrics(run, [JobDuration]);
            Log.Information("  - {Case}: traces=2 job-duration-series={Series}", name, metricsEnabled ? 2 : 0);
        }
    }
}
