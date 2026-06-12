using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Qyl.AutoInstrumentation;

var exportedMetrics = new List<Metric>();

using var meterProvider = Sdk
    .CreateMeterProviderBuilder()
    .AddMeter(QylMetricMeters.NetRuntimeMeterName)
    .AddRuntimeInstrumentation()
    .AddInMemoryExporter(exportedMetrics)
    .Build();

_ = Enumerable.Range(0, 1024)
    .Select(static index => new byte[index % 64])
    .ToArray();
GC.Collect();
GC.WaitForPendingFinalizers();
GC.Collect();

var queued = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
ThreadPool.QueueUserWorkItem(static state => ((TaskCompletionSource)state!).TrySetResult(), queued);
await queued.Task.WaitAsync(TimeSpan.FromSeconds(5));

for (var attempt = 0; attempt < 40 && exportedMetrics.Count is 0; attempt++)
{
    meterProvider.ForceFlush();
    await Task.Delay(TimeSpan.FromMilliseconds(250));
}

meterProvider.ForceFlush();

var report = NetRuntimeMetricsReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    exportedMetrics.Select(CapturedMetric.From).ToArray());

var json = JsonSerializer.Serialize(report, RealNetRuntimeMetricsJsonContext.Default.NetRuntimeMetricsReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

internal sealed record CapturedMetric(
    string MeterName,
    string Name,
    int PointCount)
{
    public static CapturedMetric From(Metric metric)
        => new(
            metric.MeterName,
            metric.Name,
            CountMetricPoints(metric));

    private static int CountMetricPoints(Metric metric)
    {
        var count = 0;
        foreach (ref readonly var _ in metric.GetMetricPoints())
            count++;

        return count;
    }
}

internal sealed record NetRuntimeMetricsReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    CapturedMetric[] Metrics)
{
    public static NetRuntimeMetricsReport Create(string runtimeMode, CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        var runtimeMetrics = metrics
            .Where(static metric => StringComparer.Ordinal.Equals(metric.MeterName, QylMetricMeters.NetRuntimeMeterName))
            .ToArray();

        if (runtimeMetrics.Length is 0)
        {
            failures.Add("expected real OpenTelemetry runtime metrics, got none");
            failures.Add("observed metrics: " + string.Join("|", metrics.Select(static metric => metric.MeterName + ":" + metric.Name).OrderBy(static name => name, StringComparer.Ordinal)));
        }

        RequireMetric(runtimeMetrics, QylMetricNames.ProcessRuntimeDotnetGcCollectionsCount, failures);
        RequireMetric(runtimeMetrics, QylMetricNames.ProcessRuntimeDotnetGcHeapSize, failures);
        RequireMetric(runtimeMetrics, QylMetricNames.ProcessRuntimeDotnetThreadPoolThreadsCount, failures);

        foreach (var metric in runtimeMetrics.Where(static metric => metric.PointCount <= 0))
            failures.Add($"expected at least one metric point for {metric.Name}, got {metric.PointCount.ToString(CultureInfo.InvariantCulture)}");

        return new NetRuntimeMetricsReport(runtimeMode, failures.Count is 0, failures.ToArray(), runtimeMetrics);
    }

    private static void RequireMetric(IEnumerable<CapturedMetric> metrics, string name, ICollection<string> failures)
    {
        if (!metrics.Any(metric => StringComparer.Ordinal.Equals(metric.Name, name)))
            failures.Add($"missing runtime metric {name}");
    }
}

[JsonSerializable(typeof(NetRuntimeMetricsReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealNetRuntimeMetricsJsonContext : JsonSerializerContext;
