namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Metric Names.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
internal static class QylMetricNames
{
    /// <summary>Well-known database Client Operation Duration value used by qyl auto-instrumentation.</summary>
    public const string DbClientOperationDuration = "db.client.operation.duration";
    /// <summary>Well-known N Service Bus Messaging Operation Duration value used by qyl auto-instrumentation.</summary>
    public const string NServiceBusMessagingOperationDuration = "nservicebus.messaging.operation.duration";

    /// <summary>Well-known Process Cpu Time value used by qyl auto-instrumentation.</summary>
    public const string ProcessCpuTime = "dotnet.process.cpu.time";
    /// <summary>Well-known Process Memory Usage value used by qyl auto-instrumentation.</summary>
    public const string ProcessMemoryUsage = "dotnet.process.memory.working_set";
    /// <summary>Well-known Process Cpu Count value used by qyl auto-instrumentation.</summary>
    public const string ProcessCpuCount = "dotnet.process.cpu.count";

    /// <summary>Well-known Process Runtime Dotnet Gc Collections Count value used by qyl auto-instrumentation.</summary>
    public const string ProcessRuntimeDotnetGcCollectionsCount = "dotnet.gc.collections";
    /// <summary>Well-known Process Runtime Dotnet Gc Heap Size value used by qyl auto-instrumentation.</summary>
    public const string ProcessRuntimeDotnetGcHeapSize = "dotnet.gc.last_collection.heap.size";
    /// <summary>Well-known Process Runtime Dotnet Gc Objects Size value used by qyl auto-instrumentation.</summary>
    public const string ProcessRuntimeDotnetGcObjectsSize = "dotnet.gc.heap.total_allocated";
    /// <summary>Well-known Process Runtime Dotnet Thread Pool Queue Length value used by qyl auto-instrumentation.</summary>
    public const string ProcessRuntimeDotnetThreadPoolQueueLength = "dotnet.thread_pool.queue.length";
    /// <summary>Well-known Process Runtime Dotnet Thread Pool Threads Count value used by qyl auto-instrumentation.</summary>
    public const string ProcessRuntimeDotnetThreadPoolThreadsCount = "dotnet.thread_pool.thread.count";

}
