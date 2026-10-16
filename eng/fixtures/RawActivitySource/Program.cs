using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;

var exported = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
builder.AddQyl(options =>
{
    options.EnableCollectorDiscovery = false;
    options.RequireConfiguredEndpoint = true;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.AdditionalSources.Add("Shop.Orders");
    options.ConfigureTracing = tracing => tracing.AddInMemoryExporter(exported);
});

using var source = new ActivitySource("Shop.Orders");
using (var host = builder.Build())
{
    await host.StartAsync();
    string[] labels = ["first", "second"];
    using (var span = source.StartActivity("checkout"))
    {
        span?.SetTag("checkout.sequence", 9_007_199_254_740_993L);
        span?.SetTag("checkout.accepted", false);
        span?.SetTag("checkout.labels", labels);
    }
    host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
    await host.StopAsync();
}

Console.WriteLine("exported=" + exported.Count);
foreach (var activity in exported)
{
    Console.WriteLine("scope=" + activity.Source.Name);
    foreach (var tag in activity.TagObjects)
        Console.WriteLine(tag.Key + "=" + tag.Value?.GetType().Name + "|" +
            (tag.Value is string[] labels ? string.Join(',', labels) : tag.Value));
}
return 0;
