using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Logs;
using Qyl;

var qylActivities = new List<string>();
var qylActivityLock = new Lock();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (qylActivityLock)
        {
            qylActivities.Add(activity.DisplayName);
        }
    },
};

ActivitySource.AddActivityListener(listener);

var exportedRecords = new List<LogRecord>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealILoggerDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-ilogger-demo";
    options.EnableCollectorDiscovery = false;
    options.RequireConfiguredEndpoint = true;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});

// Configure only: registering the OpenTelemetry logging provider is AddQyl's job, so an
// unregistered provider leaves this exporter unreachable and the record list empty.
builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddInMemoryExporter(exportedRecords));
builder.Logging.SetMinimumLevel(LogLevel.Trace);

using var host = builder.Build();

var providerRegistered = host.Services.GetServices<ILoggerProvider>().OfType<OpenTelemetryLoggerProvider>().Any();
var logger = host.Services.GetRequiredService<ILoggerFactory>().CreateLogger("Qyl.RealILoggerDemo");

logger.LogTrace("qyl trace record");
logger.LogDebug("qyl debug record");
logger.LogInformation("qyl information record");
logger.LogWarning("qyl warning record");
logger.LogError("qyl error record");
logger.LogCritical("qyl critical record");

host.Dispose();

Console.WriteLine("ilogger-record-count=" + exportedRecords.Count.ToString(CultureInfo.InvariantCulture));

var report = ILoggerReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    LogsControl.IsEnabled(),
    providerRegistered,
    exportedRecords.Select(CapturedLogRecord.From).ToArray(),
    qylActivities.ToArray());

var json = JsonSerializer.Serialize(report, RealILoggerJsonContext.Default.ILoggerReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

internal static class LogsControl
{
    internal const string Variable = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED";

    internal static bool IsEnabled()
        => Environment.GetEnvironmentVariable(Variable) is not { } value ||
           !StringComparer.OrdinalIgnoreCase.Equals(value, "false");
}

internal sealed record CapturedLogRecord(string Severity, string Body, string FormattedMessage, string CategoryName)
{
    public static CapturedLogRecord From(LogRecord record)
        => new(
            record.LogLevel.ToString(),
            Convert.ToString(record.Body, CultureInfo.InvariantCulture) ?? string.Empty,
            record.FormattedMessage ?? string.Empty,
            record.CategoryName ?? string.Empty);
}

internal sealed record ILoggerReport(
    string RuntimeMode,
    bool LogsControlEnabled,
    bool OpenTelemetryLoggerProviderRegistered,
    bool Pass,
    string[] Failures,
    CapturedLogRecord[] Records,
    string[] QylActivities)
{
    private static readonly (string Severity, string Body)[] ExpectedRecords =
    [
        ("Trace", "qyl trace record"),
        ("Debug", "qyl debug record"),
        ("Information", "qyl information record"),
        ("Warning", "qyl warning record"),
        ("Error", "qyl error record"),
        ("Critical", "qyl critical record"),
    ];

    public static ILoggerReport Create(
        string runtimeMode,
        bool logsControlEnabled,
        bool providerRegistered,
        CapturedLogRecord[] records,
        string[] qylActivities)
    {
        var failures = new List<string>();

        // The regression assertion for the deleted log-as-span lane: an ILogger call must produce a
        // LogRecord and nothing on the qyl ActivitySource.
        if (qylActivities.Length is not 0)
            failures.Add($"expected no qyl activity for a log call, got {string.Join("|", qylActivities)}");

        if (logsControlEnabled)
            RequireExport(providerRegistered, records, failures);
        else
            RequireNoExport(providerRegistered, records, failures);

        return new ILoggerReport(
            runtimeMode,
            logsControlEnabled,
            providerRegistered,
            failures.Count is 0,
            failures.ToArray(),
            records,
            qylActivities);
    }

    private static void RequireExport(bool providerRegistered, CapturedLogRecord[] records, ICollection<string> failures)
    {
        if (!providerRegistered)
            failures.Add("AddQyl did not register the OpenTelemetry logging provider");

        if (records.Length != ExpectedRecords.Length)
        {
            failures.Add($"expected {ExpectedRecords.Length} exported log records, got {records.Length}");
            return;
        }

        for (var index = 0; index < ExpectedRecords.Length; index++)
        {
            var (severity, body) = ExpectedRecords[index];
            if (!StringComparer.Ordinal.Equals(records[index].Severity, severity))
                failures.Add($"record {index} severity: expected {severity}, got {records[index].Severity}");
            if (!StringComparer.Ordinal.Equals(records[index].Body, body))
                failures.Add($"record {index} body: expected {body}, got {records[index].Body}");
            if (!StringComparer.Ordinal.Equals(records[index].FormattedMessage, body))
                failures.Add($"record {index} formatted message: expected {body}, got {records[index].FormattedMessage}");
            if (!StringComparer.Ordinal.Equals(records[index].CategoryName, "Qyl.RealILoggerDemo"))
                failures.Add($"record {index} category: got {records[index].CategoryName}");
        }
    }

    private static void RequireNoExport(bool providerRegistered, CapturedLogRecord[] records, ICollection<string> failures)
    {
        if (providerRegistered)
            failures.Add($"{LogsControl.Variable}=false must leave the OpenTelemetry logging provider unregistered");
        if (records.Length is not 0)
            failures.Add($"{LogsControl.Variable}=false must export no log records, got {records.Length}");
    }
}

[JsonSerializable(typeof(ILoggerReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealILoggerJsonContext : JsonSerializerContext;
