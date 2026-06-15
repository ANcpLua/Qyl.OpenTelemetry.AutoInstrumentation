using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using NLog;
using NLog.Config;
using NLog.Targets;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var memoryTarget = new MemoryTarget("memory");
var configuration = new LoggingConfiguration();
configuration.AddRule(LogLevel.Info, LogLevel.Fatal, memoryTarget);
LogManager.Configuration = configuration;

var logger = LogManager.GetLogger("qyl.nlog");
logger.Trace("qyl disabled trace");
logger.Info("qyl information");
logger.Error("qyl error");
LogManager.Flush();

Console.WriteLine("nlog-memory-count=" + memoryTarget.Logs.Count.ToString(CultureInfo.InvariantCulture));

var report = NLogReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    memoryTarget.Logs.ToArray(),
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealNLogJsonContext.Default.NLogReport);
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

internal sealed record NLogReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] LogLines,
    CapturedActivity[] Activities)
{
    public static NLogReport Create(string runtimeMode, string[] logLines, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var nlogSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain) &&
                StringComparer.Ordinal.Equals(domain, QylInstrumentationDomains.LogNLog))
            .ToArray();

        if (logLines.Length != 2)
            failures.Add($"expected 2 NLog output records, got {logLines.Length}");

        if (nlogSpans.Length != 2)
            failures.Add($"expected 2 NLog spans, got {nlogSpans.Length}");

        var information = FindBySeverity(nlogSpans, QylSemanticAttributes.LogSeverityInformation);
        var error = FindBySeverity(nlogSpans, QylSemanticAttributes.LogSeverityError);
        Require(information, "information span", failures);
        Require(error, "error span", failures);

        foreach (var span in nlogSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "NLog log"))
                failures.Add($"unexpected NLog span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");

            if (!StringComparer.Ordinal.Equals(span.Status, "Unset"))
                failures.Add($"expected status Unset, got {span.Status}");
        }

        return new NLogReport(runtimeMode, failures.Count is 0, failures.ToArray(), logLines, nlogSpans);
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

[JsonSerializable(typeof(NLogReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealNLogJsonContext : JsonSerializerContext;
