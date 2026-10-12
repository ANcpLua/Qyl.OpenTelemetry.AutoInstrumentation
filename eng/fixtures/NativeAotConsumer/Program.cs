using System.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Qyl;

// The gate proves the qyl runtime still works inside a NativeAOT-published consumer. Until 15.0.0
// it did that by writing a synthetic event into the HttpClient DiagnosticListener, which qyl
// subscribed. That lane is gone — the DiagnosticListeners package no longer ships a single concrete
// subscriber — so the consumer now exercises the lane the release kept: System.Net.Http emits the
// span, and the processor AddQyl registers stamps the qyl domain onto it. The request goes to a
// closed loopback port, so the proof needs no server and stays deterministic.
var exported = new List<Activity>();
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-nativeaot-consumer";
    options.CollectorEndpoint = new Uri("http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
});
builder.Services.AddOpenTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(exported));

using var host = builder.Build();
await host.StartAsync();

using (var http = new HttpClient())
{
    try
    {
        using var response = await http.GetAsync("http://127.0.0.1:1/nativeaot/client?id=42");
    }
    catch (HttpRequestException)
    {
        // The refused connection is the point: it produces a client span with an error, without a server.
    }
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000);
await host.StopAsync();

var captured = exported
    .Where(static activity =>
        activity.GetTagItem("qyl.instrumentation.domain") is "http.client")
    .ToList();

if (captured.Count != 1)
{
    Console.WriteLine("captured.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));
    return 1;
}

var activity = captured[0];
Console.WriteLine("name=" + activity.DisplayName);
Console.WriteLine("kind=" + activity.Kind);

foreach (var tag in activity.TagObjects.OrderBy(static tag => tag.Key, StringComparer.Ordinal))
{
    Console.WriteLine(tag.Key + "=" + Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture));
}

return 0;
