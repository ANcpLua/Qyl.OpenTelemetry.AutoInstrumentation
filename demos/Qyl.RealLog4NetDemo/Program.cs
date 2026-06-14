using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using log4net;
using log4net.Appender;
using log4net.Core;
using log4net.Repository.Hierarchy;
using Qyl.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var memoryAppender = new MemoryAppender { Name = "memory" };
memoryAppender.ActivateOptions();

var repositoryAssembly = typeof(RealLog4NetJsonContext).Assembly;
var hierarchy = (Hierarchy)LogManager.GetRepository(repositoryAssembly);
hierarchy.Root.RemoveAllAppenders();
hierarchy.Root.Level = Level.Info;
hierarchy.Root.AddAppender(memoryAppender);
hierarchy.Configured = true;

ILog logger = LogManager.GetLogger(repositoryAssembly, "qyl.log4net");
logger.Debug("qyl disabled debug");
logger.Info("qyl information");
logger.Error("qyl error");

var logEvents = memoryAppender.GetEvents();
Console.WriteLine("log4net-memory-count=" + logEvents.Length.ToString(CultureInfo.InvariantCulture));

var report = Log4NetReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    logEvents.Select(static logEvent => logEvent.RenderedMessage ?? string.Empty).ToArray(),
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealLog4NetJsonContext.Default.Log4NetReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

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

internal sealed record Log4NetReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] LogLines,
    CapturedActivity[] Activities)
{
    public static Log4NetReport Create(string runtimeMode, string[] logLines, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var log4NetSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.LogLog4Net))
            .ToArray();

        if (logLines.Length != 2)
            failures.Add($"expected 2 log4net output records, got {logLines.Length}");

        if (log4NetSpans.Length != 2)
            failures.Add($"expected 2 log4net spans, got {log4NetSpans.Length}");

        var information = FindBySeverity(log4NetSpans, QylSemanticAttributes.LogSeverityInformation);
        var error = FindBySeverity(log4NetSpans, QylSemanticAttributes.LogSeverityError);
        Require(information, "information span", failures);
        Require(error, "error span", failures);

        foreach (var span in log4NetSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "log4net log"))
                failures.Add($"unexpected log4net span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");

            if (!StringComparer.Ordinal.Equals(span.Status, "Unset"))
                failures.Add($"expected status Unset, got {span.Status}");
        }

        return new Log4NetReport(runtimeMode, failures.Count is 0, failures.ToArray(), logLines, log4NetSpans);
    }

    private static CapturedActivity? FindBySeverity(IEnumerable<CapturedActivity> activities, string severity)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue(QylSemanticAttributes.LogSeverity, out var actual) &&
            StringComparer.Ordinal.Equals(actual, severity));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }
}

[JsonSerializable(typeof(Log4NetReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealLog4NetJsonContext : JsonSerializerContext;
