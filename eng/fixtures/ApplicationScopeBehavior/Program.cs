using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;

// The application path: no ActivitySource and no AddSource. AddQyl subscribes the registry-owned
// application scope behind Qyl.Spans.Source, so a Spans.Work span reaches the exporter as it is.
var exported = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.ApplicationScopeBehavior",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-application-scope-behavior";
    options.EnableCollectorDiscovery = false;
    options.RequireConfiguredEndpoint = true;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
});

using (var host = builder.Build())
{
    await host.StartAsync();
    using (var span = Spans.Work("checkout"))
        span?.SetAppTag("order.id", "o-1");

    host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
    await host.StopAsync();
}

Console.WriteLine("exported=" + exported.Count);
foreach (var activity in exported)
{
    Console.WriteLine($"span={activity.DisplayName}|{activity.Kind}");
    Console.WriteLine($"scope={activity.Source.Name}|{activity.Source.Version}|{activity.Source.TelemetrySchemaUrl}");
    Console.WriteLine("qyl.instrumentation.domain=" + activity.GetTagItem("qyl.instrumentation.domain"));
    Console.WriteLine("qyl.app.order.id=" + activity.GetTagItem("qyl.app.order.id"));
}

return 0;
