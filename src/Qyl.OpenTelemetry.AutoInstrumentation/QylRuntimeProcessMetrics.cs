using System.Diagnostics;
using System.Diagnostics.Metrics;

using Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode;

namespace Qyl.OpenTelemetry.AutoInstrumentation;

internal static class QylRuntimeProcessMetrics
{
    public static void Initialize()
    {
        var options = QylAutoInstrumentationOptions.Current;

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NetRuntime))
            NetRuntimeMetrics.Initialize();

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Process))
            ProcessMetrics.Initialize();
    }

    private static class NetRuntimeMetrics
    {
        private static readonly KeyValuePair<string, object?>[] Gen0Tags =
        [
            new(QylSemanticAttributes.DotnetGcHeapGeneration, QylSemanticAttributes.DotnetGcHeapGenerationGen0),
        ];

        private static readonly KeyValuePair<string, object?>[] Gen1Tags =
        [
            new(QylSemanticAttributes.DotnetGcHeapGeneration, QylSemanticAttributes.DotnetGcHeapGenerationGen1),
        ];

        private static readonly KeyValuePair<string, object?>[] Gen2Tags =
        [
            new(QylSemanticAttributes.DotnetGcHeapGeneration, QylSemanticAttributes.DotnetGcHeapGenerationGen2),
        ];

        private static readonly Measurement<long>[] GcCollectionMeasurements = new Measurement<long>[3];

        private static readonly Meter Meter = new(QylMetricMeters.NetRuntimeMeterName);
        private static readonly ObservableCounter<long> GcCollections = Meter.CreateObservableCounter(
            QylMetricNames.ProcessRuntimeDotnetGcCollectionsCount,
            ObserveGcCollections);
        private static readonly ObservableGauge<long> GcHeapSize = Meter.CreateObservableGauge(
            QylMetricNames.ProcessRuntimeDotnetGcHeapSize,
            static () => GC.GetGCMemoryInfo().HeapSizeBytes,
            "By");
        private static readonly ObservableGauge<long> GcObjectsSize = Meter.CreateObservableGauge(
            QylMetricNames.ProcessRuntimeDotnetGcObjectsSize,
            static () => GC.GetTotalMemory(false),
            "By");
        // OTel semconv: dotnet.thread_pool.thread.count is an (Observable)UpDownCounter with unit {thread},
        // not a unitless gauge — the pool size goes up and down, and UCUM units are required.
        private static readonly ObservableUpDownCounter<int> ThreadPoolThreads = Meter.CreateObservableUpDownCounter(
            QylMetricNames.ProcessRuntimeDotnetThreadPoolThreadsCount,
            static () => ThreadPool.ThreadCount,
            unit: "{thread}");
        private static readonly ObservableGauge<long> ThreadPoolQueueLength = Meter.CreateObservableGauge(
            QylMetricNames.ProcessRuntimeDotnetThreadPoolQueueLength,
            static () => ThreadPool.PendingWorkItemCount);

        public static void Initialize()
        {
            _ = GcCollections;
            _ = GcHeapSize;
            _ = GcObjectsSize;
            _ = ThreadPoolThreads;
            _ = ThreadPoolQueueLength;
        }

        private static Measurement<long>[] ObserveGcCollections()
        {
            GcCollectionMeasurements[0] = new Measurement<long>(GC.CollectionCount(0), Gen0Tags);
            GcCollectionMeasurements[1] = new Measurement<long>(GC.CollectionCount(1), Gen1Tags);
            GcCollectionMeasurements[2] = new Measurement<long>(GC.CollectionCount(2), Gen2Tags);

            return GcCollectionMeasurements;
        }
    }

    private static class ProcessMetrics
    {
        private static readonly KeyValuePair<string, object?>[] UserCpuModeTags =
        [
            new(QylSemanticAttributes.CpuMode, QylSemanticAttributes.CpuModeUser),
        ];

        private static readonly KeyValuePair<string, object?>[] SystemCpuModeTags =
        [
            new(QylSemanticAttributes.CpuMode, QylSemanticAttributes.CpuModeSystem),
        ];

        private static readonly Measurement<double>[] CpuTimeMeasurements = new Measurement<double>[2];

        private static readonly Meter Meter = new(QylMetricMeters.ProcessMeterName);
        private static readonly ObservableCounter<double> CpuTime = Meter.CreateObservableCounter(
            QylMetricNames.ProcessCpuTime,
            ObserveCpuTime,
            "s");
        private static readonly ObservableGauge<long> MemoryUsage = Meter.CreateObservableGauge(
            QylMetricNames.ProcessMemoryUsage,
            static () => Environment.WorkingSet,
            "By");
        private static readonly ObservableGauge<int> CpuCount = Meter.CreateObservableGauge(
            QylMetricNames.ProcessCpuCount,
            static () => Environment.ProcessorCount,
            "{cpu}");

        public static void Initialize()
        {
            _ = CpuTime;
            _ = MemoryUsage;
            _ = CpuCount;
        }

        private static Measurement<double>[] ObserveCpuTime()
        {
            using var process = Process.GetCurrentProcess();

            CpuTimeMeasurements[0] = new Measurement<double>(process.UserProcessorTime.TotalSeconds, UserCpuModeTags);
            CpuTimeMeasurements[1] = new Measurement<double>(process.PrivilegedProcessorTime.TotalSeconds, SystemCpuModeTags);

            return CpuTimeMeasurements;
        }
    }
}
