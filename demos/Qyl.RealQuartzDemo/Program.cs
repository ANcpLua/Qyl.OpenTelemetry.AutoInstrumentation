using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Quartz;
using Quartz.Impl;
using Qyl.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var factory = new StdSchedulerFactory();
var scheduler = await factory.GetScheduler();
await scheduler.Start();

var job = JobBuilder.Create<OuterJob>().WithIdentity("qyl-outer").Build();
var trigger = TriggerBuilder.Create().WithIdentity("qyl-now").StartNow().Build();
await scheduler.ScheduleJob(job, trigger);

await OuterJob.Completed.Task.WaitAsync(TimeSpan.FromSeconds(30));
await scheduler.Shutdown(waitForJobsToComplete: true);
Console.WriteLine("scheduler-fired=true");

var report = QuartzReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealQuartzJsonContext.Default.QuartzReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

/// <summary>Delegation target job whose source-visible Execute call is intercepted.</summary>
public sealed class ProbeJob : IJob
{
    /// <inheritdoc />
    public Task Execute(IJobExecutionContext context) => Task.CompletedTask;
}

/// <summary>Failing delegation target proving the interceptor error path.</summary>
public sealed class FailingJob : IJob
{
    /// <inheritdoc />
    public Task Execute(IJobExecutionContext context) => throw new InvalidOperationException("qyl-quartz-error");
}

/// <summary>Scheduler-fired job that delegates to inner jobs through source-visible calls.</summary>
public sealed class OuterJob : IJob
{
    /// <summary>Signals that the scheduler executed this job to completion.</summary>
    public static TaskCompletionSource Completed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <inheritdoc />
    public async Task Execute(IJobExecutionContext context)
    {
        IJob probe = new ProbeJob();
        await probe.Execute(context);

        IJob failing = new FailingJob();
        try
        {
            await failing.Execute(context);
        }
        catch (InvalidOperationException exception)
        {
            Console.WriteLine("expected-quartz-error=" + exception.Message);
        }

        Completed.TrySetResult();
    }
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
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
    public static QuartzReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var quartzSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.JobQuartz))
            .ToArray();

        if (quartzSpans.Length != 2)
            failures.Add($"expected 2 Quartz execute spans, got {quartzSpans.Length}");

        var success = quartzSpans.FirstOrDefault(static span => StringComparer.Ordinal.Equals(span.Status, "Unset"));
        var error = quartzSpans.FirstOrDefault(static span => StringComparer.Ordinal.Equals(span.Status, "Error"));

        if (success is null)
            failures.Add("missing successful Quartz execute span");

        if (error is null)
            failures.Add("missing error Quartz execute span");
        else if (!error.Tags.TryGetValue(QylSemanticAttributes.ErrorType, out var errorType) ||
                 !StringComparer.Ordinal.Equals(errorType, "InvalidOperationException"))
            failures.Add("expected error.type=InvalidOperationException on error span");

        foreach (var span in quartzSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "Quartz execute"))
                failures.Add($"unexpected Quartz span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");
        }

        return new QuartzReport(runtimeMode, failures.Count is 0, failures.ToArray(), quartzSpans);
    }
}

[JsonSerializable(typeof(QuartzReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealQuartzJsonContext : JsonSerializerContext;
