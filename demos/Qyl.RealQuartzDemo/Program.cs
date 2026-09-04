using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Quartz;
using Qyl;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using QuartzAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Quartz.QuartzAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

// The real registration path: AddQyl subscribes to Quartz's own ActivitySource and installs the one
// native-span processor.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealQuartzDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-quartz-demo";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

await using var factory = QuartzSchedulerBuilder
    .Create(schedulerBuilder => schedulerBuilder.UseDefaultThreadPool().UseInMemoryStore())
    .Build();

var scheduler = await factory.GetScheduler();
await scheduler.Start();

await scheduler.ScheduleJob(
    JobBuilder.Create<ProbeJob>().WithIdentity(QuartzReport.ProbeJobName).Build(),
    TriggerBuilder.Create().WithIdentity("qyl-probe-now").StartNow().Build());
await scheduler.ScheduleJob(
    JobBuilder.Create<FailingJob>().WithIdentity(QuartzReport.FailingJobName).Build(),
    TriggerBuilder.Create().WithIdentity("qyl-failing-now").StartNow().Build());

await ProbeJob.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
await FailingJob.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
await scheduler.Shutdown(waitForJobsToComplete: true);
Console.WriteLine("scheduler-fired=true");

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = QuartzReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealQuartzJsonContext.Default.QuartzReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

/// <summary>Scheduler-fired job whose execution the native source traces.</summary>
public sealed class ProbeJob : IJob
{
    /// <summary>Signals that the scheduler executed this job to completion.</summary>
    public static TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        Completed.TrySetResult();
        return ValueTask.CompletedTask;
    }
}

/// <summary>Scheduler-fired job that fails, proving the native source records error.type itself.</summary>
public sealed class FailingJob : IJob
{
    /// <summary>Signals that the scheduler executed this job.</summary>
    public static TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public ValueTask Execute(IJobExecutionContext context, CancellationToken cancellationToken)
    {
        Completed.TrySetResult();
        throw new InvalidOperationException("qyl-quartz-error");
    }
}

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record QuartzReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedActivity[] Activities)
{
    internal const string ProbeJobName = "qyl-probe";
    internal const string FailingJobName = "qyl-failing";

    // Quartz's own name for the span covering one firing, Quartz.Diagnostics.OperationName.Job.Execute.
    private const string ExecuteSpanName = "Quartz.Job.Execute";

    public static QuartzReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();

        // The scheduler also traces every call into its job store on the same source. The job
        // firings are the Quartz.Job.Execute spans, and each scheduled job must produce exactly one.
        var executeSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.Quartz))
            .Where(static activity => StringComparer.Ordinal.Equals(activity.Name, ExecuteSpanName))
            .ToArray();

        foreach (var jobName in new[] { ProbeJobName, FailingJobName })
        {
            var matching = executeSpans
                .Where(span =>
                    span.Tags.TryGetValue(QuartzAttributes.JobName, out var name) &&
                    StringComparer.Ordinal.Equals(name, jobName))
                .ToArray();
            if (matching.Length != 1)
                failures.Add($"expected exactly 1 Quartz execute span for '{jobName}', got {matching.Length.ToString(CultureInfo.InvariantCulture)}");
        }

        foreach (var span in executeSpans)
        {
            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.JobQuartz,
                failures);

            // Quartz's own attributes. It publishes no messaging or database conventions: the
            // vendor quartz.* namespace and error.type are the whole vocabulary.
            RequirePresentTag(span, QuartzAttributes.JobName, failures);
            RequirePresentTag(span, QuartzAttributes.JobGroup, failures);
            RequirePresentTag(span, QuartzAttributes.JobType, failures);
            RequirePresentTag(span, QuartzAttributes.SchedulerName, failures);
            RequirePresentTag(span, QuartzAttributes.FireInstanceId, failures);
        }

        var failingSpan = executeSpans.FirstOrDefault(static span =>
            span.Tags.TryGetValue(QuartzAttributes.JobName, out var name) &&
            StringComparer.Ordinal.Equals(name, FailingJobName));
        if (failingSpan is null)
            failures.Add("missing the failing job's execute span");
        else
            RequireTag(failingSpan, ErrorAttributes.Type, typeof(InvalidOperationException).FullName!, failures);

        return new QuartzReport(runtimeMode, failures.Count is 0, failures.ToArray(), executeSpans);
    }

    private static void RequireTag(CapturedActivity span, string key, string expected, ICollection<string> failures)
    {
        if (!span.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }

    private static void RequirePresentTag(CapturedActivity span, string key, ICollection<string> failures)
    {
        if (!span.Tags.ContainsKey(key))
            failures.Add($"missing {key}");
    }
}

[JsonSerializable(typeof(QuartzReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealQuartzJsonContext : JsonSerializerContext;
