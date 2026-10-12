using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Trace;
using Qyl;

if (args.Length != 2)
    throw new InvalidOperationException("Expected OTLP and downstream endpoints.");

// The consumer onboards the way the README tells it to. Since 15.0.0 the HttpClient span
// is System.Net.Http's own, and the qyl domain is stamped onto it by the processor AddQyl
// registers — a bare TracerProviderBuilder subscribing the qyl source alone now sees
// nothing at all, which is what this gate started failing on.
var builder = Host.CreateApplicationBuilder(args);
builder.Logging.ClearProviders();
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-otlp-receiver-consumer";
    options.CollectorEndpoint = new Uri(args[0]);
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
});

using var host = builder.Build();
await host.StartAsync();

using (var http = new HttpClient())
using (var response = await http.GetAsync(args[1]))
{
    if ((int)response.StatusCode != 204)
        throw new InvalidOperationException("Loopback downstream returned an unexpected status.");
}

if (!host.Services.GetRequiredService<TracerProvider>().ForceFlush(10_000))
    throw new InvalidOperationException("OTLP trace export did not flush.");

await host.StopAsync();
