using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

var logLines = new List<string>();
ILogger logger = new ProbeLogger(logLines);

logger.LogDebug("qyl disabled debug");
logger.Log(LogLevel.Information, new EventId(1, "info"), "qyl information", null, static (state, _) => state);
logger.LogError(new InvalidOperationException("qyl boom"), "qyl error");

Console.WriteLine("ilogger-output-count=" + logLines.Count.ToString(CultureInfo.InvariantCulture));

var report = ILoggerReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    logLines.ToArray(),
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealILoggerJsonContext.Default.ILoggerReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

internal sealed class ProbeLogger(List<string> logLines) : ILogger
{
    public IDisposable? BeginScope<TState>(TState state)
        where TState : notnull
        => NullScope.Instance;

    public bool IsEnabled(LogLevel logLevel)
        => logLevel >= LogLevel.Information;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        if (!IsEnabled(logLevel))
            return;

        logLines.Add(logLevel + ":" + formatter(state, exception));
    }

    private sealed class NullScope : IDisposable
    {
        public static readonly NullScope Instance = new();

        public void Dispose()
        {
        }
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

internal sealed record ILoggerReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] LogLines,
    CapturedActivity[] Activities)
{
    public static ILoggerReport Create(string runtimeMode, string[] logLines, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var loggerSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "log.ilogger"))
            .ToArray();

        if (logLines.Length != 2)
            failures.Add($"expected 2 enabled ILogger output records, got {logLines.Length}");

        if (loggerSpans.Length != 2)
            failures.Add($"expected 2 ILogger spans, got {loggerSpans.Length}");

        var information = FindBySeverity(loggerSpans, "Information");
        var error = FindBySeverity(loggerSpans, "Error");
        Require(information, "information span", failures);
        Require(error, "error span", failures);
        RequireTag(error, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(InvalidOperationException).FullName!, failures);

        foreach (var span in loggerSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "ILogger log"))
                failures.Add($"unexpected ILogger span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");
        }

        return new ILoggerReport(runtimeMode, failures.Count is 0, failures.ToArray(), logLines, loggerSpans);
    }

    private static CapturedActivity? FindBySeverity(IEnumerable<CapturedActivity> activities, string severity)
        => activities.FirstOrDefault(activity =>
            activity.Tags.TryGetValue("log.severity", out var actual) &&
            StringComparer.Ordinal.Equals(actual, severity));

    private static void Require(CapturedActivity? activity, string label, ICollection<string> failures)
    {
        if (activity is null)
            failures.Add($"missing {label}");
    }

    private static void RequireTag(CapturedActivity? activity, string key, string expected, ICollection<string> failures)
    {
        if (activity is null)
            return;

        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(ILoggerReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealILoggerJsonContext : JsonSerializerContext;
