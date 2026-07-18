namespace Qyl.OpenTelemetry.AutoInstrumentation;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Metric Names.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
internal static class QylMetricNames
{
    /// <summary>Well-known HTTP Server Request Duration value used by qyl auto-instrumentation.</summary>
    public const string HttpServerRequestDuration = "http.server.request.duration";
    /// <summary>Well-known ASP.NET Core Components Navigate value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsNavigate = "aspnetcore.components.navigate";
    /// <summary>Well-known ASP.NET Core Components Handle Event Duration value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsHandleEventDuration = "aspnetcore.components.handle_event.duration";
    /// <summary>Well-known ASP.NET Core Components Update Parameters Duration value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsUpdateParametersDuration = "aspnetcore.components.update_parameters.duration";
    /// <summary>Well-known ASP.NET Core Components Render Diff Duration value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsRenderDiffDuration = "aspnetcore.components.render_diff.duration";
    /// <summary>Well-known ASP.NET Core Components Render Diff Size value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsRenderDiffSize = "aspnetcore.components.render_diff.size";

    /// <summary>Well-known database Client Operation Duration value used by qyl auto-instrumentation.</summary>
    public const string DbClientOperationDuration = "db.client.operation.duration";
    /// <summary>Well-known HTTP Client Request Duration value used by qyl auto-instrumentation.</summary>
    public const string HttpClientRequestDuration = "http.client.request.duration";
    /// <summary>Well-known DNS Lookup Duration value used by qyl auto-instrumentation.</summary>
    public const string DnsLookupDuration = "dns.lookup.duration";
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
