using System.Diagnostics;
using System.Diagnostics.Metrics;

namespace Qyl.AutoInstrumentation;

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
        private static readonly Meter Meter = new("OpenTelemetry.Instrumentation.Runtime");
        private static readonly ObservableCounter<long> GcCollections = Meter.CreateObservableCounter(
            "process.runtime.dotnet.gc.collections.count",
            ObserveGcCollections);
        private static readonly ObservableGauge<long> GcHeapSize = Meter.CreateObservableGauge(
            "process.runtime.dotnet.gc.heap.size",
            static () => GC.GetGCMemoryInfo().HeapSizeBytes,
            "By");
        private static readonly ObservableGauge<long> GcObjectsSize = Meter.CreateObservableGauge(
            "process.runtime.dotnet.gc.objects.size",
            static () => GC.GetTotalMemory(false),
            "By");
        private static readonly ObservableGauge<int> ThreadPoolThreads = Meter.CreateObservableGauge(
            "process.runtime.dotnet.thread_pool.threads.count",
            static () => ThreadPool.ThreadCount);
        private static readonly ObservableGauge<long> ThreadPoolQueueLength = Meter.CreateObservableGauge(
            "process.runtime.dotnet.thread_pool.queue.length",
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
            =>
            [
                new(GC.CollectionCount(0), new KeyValuePair<string, object?>("generation", "gen0")),
                new(GC.CollectionCount(1), new KeyValuePair<string, object?>("generation", "gen1")),
                new(GC.CollectionCount(2), new KeyValuePair<string, object?>("generation", "gen2")),
            ];
    }

    private static class ProcessMetrics
    {
        private static readonly Meter Meter = new("OpenTelemetry.Instrumentation.Process");
        private static readonly ObservableCounter<double> CpuTime = Meter.CreateObservableCounter(
            "process.cpu.time",
            ObserveCpuTime,
            "s");
        private static readonly ObservableGauge<long> MemoryUsage = Meter.CreateObservableGauge(
            "process.memory.usage",
            static () => Environment.WorkingSet,
            "By");
        private static readonly ObservableGauge<long> MemoryVirtual = Meter.CreateObservableGauge(
            "process.memory.virtual",
            ObserveVirtualMemory,
            "By");

        public static void Initialize()
        {
            _ = CpuTime;
            _ = MemoryUsage;
            _ = MemoryVirtual;
        }

        private static Measurement<double>[] ObserveCpuTime()
        {
            using var process = Process.GetCurrentProcess();

            return
            [
                new(process.UserProcessorTime.TotalSeconds, new KeyValuePair<string, object?>("state", "user")),
                new(process.PrivilegedProcessorTime.TotalSeconds, new KeyValuePair<string, object?>("state", "system")),
            ];
        }

        private static long ObserveVirtualMemory()
        {
            using var process = Process.GetCurrentProcess();
            return process.VirtualMemorySize64;
        }
    }
}
