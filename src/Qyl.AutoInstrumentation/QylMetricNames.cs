namespace Qyl.AutoInstrumentation;

public static class QylMetricNames
{
    public const string DbClientOperationDuration = "db.client.operation.duration";
    public const string HttpClientRequestDuration = "http.client.request.duration";
    public const string NServiceBusMessagingOperationDuration = "nservicebus.messaging.operation.duration";

    public const string ProcessCpuTime = "process.cpu.time";
    public const string ProcessMemoryUsage = "process.memory.usage";
    public const string ProcessMemoryVirtual = "process.memory.virtual";

    public const string ProcessRuntimeDotnetGcCollectionsCount = "process.runtime.dotnet.gc.collections.count";
    public const string ProcessRuntimeDotnetGcHeapSize = "process.runtime.dotnet.gc.heap.size";
    public const string ProcessRuntimeDotnetGcObjectsSize = "process.runtime.dotnet.gc.objects.size";
    public const string ProcessRuntimeDotnetThreadPoolQueueLength = "process.runtime.dotnet.thread_pool.queue.length";
    public const string ProcessRuntimeDotnetThreadPoolThreadsCount = "process.runtime.dotnet.thread_pool.threads.count";

    public const string QylSemConvAttributeChecks = "qyl.semconv.attribute.checks";
    public const string QylSemConvProcessorFailures = "qyl.semconv.processor.failures";
}
