using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry;
using OpenTelemetry.Trace;
using Qyl;

// The application path: no ActivitySource and no AddSource. AddQyl subscribes the registry-owned
// application scope behind Qyl.Spans.Source, so a Spans.Work span reaches the exporter as it is. The
// resource comes from the build and the host: service.version is this build's own version, which
// the generated QylConsumerBuild hands over, and deployment.environment.name the host's environment.
var exported = new List<Activity>();
var resource = new Dictionary<string, object>(StringComparer.Ordinal);
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.ApplicationScopeBehavior",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-application-scope-behavior";
    options.ServiceNamespace = "shop";
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
        span?.SetTag("checkout.order.id", "o-1");

    var tracerProvider = host.Services.GetRequiredService<TracerProvider>();
    tracerProvider.ForceFlush(5_000);
    foreach (var attribute in tracerProvider.GetResource().Attributes)
        resource[attribute.Key] = attribute.Value;
    await host.StopAsync();
}

Console.WriteLine("exported=" + exported.Count);
foreach (var activity in exported)
{
    Console.WriteLine($"span={activity.DisplayName}|{activity.Kind}");
    Console.WriteLine($"scope={activity.Source.Name}|{activity.Source.Version}|{activity.Source.TelemetrySchemaUrl}");
    Console.WriteLine("qyl.instrumentation.domain=" + activity.GetTagItem("qyl.instrumentation.domain"));
    Console.WriteLine("checkout.order.id=" + activity.GetTagItem("checkout.order.id"));
}

foreach (var key in new[] { "service.name", "service.namespace", "service.version", "deployment.environment.name" })
    Console.WriteLine(key + "=" + (resource.TryGetValue(key, out var value) ? value : "absent"));

return 0;
