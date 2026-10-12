using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;

// A local OTLP/HTTP endpoint that counts the export requests it receives.
var listener = new HttpListener();
var port = GetFreePort();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
var exportRequests = 0;
var listening = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext context;
        try { context = await listener.GetContextAsync(); } catch { return; }
        if (context.Request.Url!.AbsolutePath.EndsWith("/v1/traces", StringComparison.Ordinal))
            Interlocked.Increment(ref exportRequests);
        context.Response.StatusCode = 200;
        context.Response.Close();
    }
});

// 1. AddQyl with discovery on and nothing listening on the conventional ports: it must not wait for
//    the probe. Measured on a builder of its own, because the marker makes a second call on the
//    same builder a no-op.
//
//    The warm-up matters and is not a fudge: the first AddQyl in a process also loads and jits the
//    OpenTelemetry registration path, which costs several hundred milliseconds on its own and would
//    swamp the thing under test. The warm-up runs with discovery OFF, so it pays the assembly cost
//    without touching the probe; the timed call is then the first one that starts the probe, and
//    its elapsed time is the probe's cost and nothing else.
var warmup = NewBuilder();
warmup.AddQyl(o =>
{
    o.ServiceName = "qyl-addqyl-warmup";
    o.EnableCollectorDiscovery = false;
    o.CollectorEndpoint = new Uri("http://127.0.0.1:1");
});

var timedBuilder = NewBuilder();
var stopwatch = Stopwatch.StartNew();
timedBuilder.AddQyl(o => o.ServiceName = "qyl-addqyl-timing");
stopwatch.Stop();
var addQylMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

// 2. Two calls, one pipeline. The second call also asks for a different service name and an extra
//    source; first call wins, and nothing it registered is duplicated.
var builder = NewBuilder();
builder.AddQyl(o =>
{
    o.ServiceName = "qyl-addqyl-behavior";
    o.CollectorEndpoint = new Uri($"http://127.0.0.1:{port}");
    o.EnableCollectorDiscovery = false;
    o.EnableLogExport = false;
    o.EnableMetricsExport = false;
    o.EnableSessionTagInheritance = false;
    // 3. A source qyl already subscribes, named again by the consumer.
    o.AdditionalSources.Add("System.Net.Http");
});
// A repeated call WITHOUT options is the AddQylApi composition: ignored, nothing duplicated.
builder.AddQyl();

// A repeated call WITH options is the 16.0.0 contract: those options cannot be honoured, and
// discarding them silently is what 15.0.0 did. It has to say so.
var rejected = false;
try
{
    builder.AddQyl(o => o.ServiceName = "second-call-with-options-must-throw");
}
catch (InvalidOperationException)
{
    rejected = true;
}

if (!rejected)
{
    Console.WriteLine("second AddQyl with options was accepted");
    return 1;
}

using var host = builder.Build();
await host.StartAsync();

using (var source = new ActivitySource("Qyl.Telemetry.AutoInstrumentation"))
using (var activity = source.StartActivity("addqyl-probe", ActivityKind.Internal))
{
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();
await Task.Delay(250);
var exportsAfterNormalHost = Volatile.Read(ref exportRequests);

// 4. The same pipeline under the build-time document host, pointed at the listener BOTH ways: the
//    environment variable it inherits from the developer, and an endpoint configured in code, which
//    is what Qyl.Api does. Neither may produce a single request. Asserting the explicit endpoint too
//    is deliberate: it is the stronger claim, and without it the scenario passes even when the gate
//    is removed, because a null endpoint leaves the exporter on a default nothing is listening on.
Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", $"http://127.0.0.1:{port}");
var buildTime = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "GetDocument.Insider",
    DisableDefaults = true,
});
buildTime.AddQyl(o =>
{
    o.EnableCollectorDiscovery = false;
    o.CollectorEndpoint = new Uri($"http://127.0.0.1:{port}");
});
using (var buildTimeHost = buildTime.Build())
{
    await buildTimeHost.StartAsync();
    using (var source = new ActivitySource("Qyl.Telemetry.AutoInstrumentation"))
    using (var activity = source.StartActivity("build-time-probe", ActivityKind.Internal))
    {
    }

    buildTimeHost.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
    await buildTimeHost.StopAsync();
}

await Task.Delay(250);
listener.Stop();

var report = new Dictionary<string, object>
{
    ["AddQylMilliseconds"] = Math.Round(addQylMilliseconds, 1),
    ["ExportRequests"] = exportsAfterNormalHost,
    ["BuildTimeExportRequests"] = Volatile.Read(ref exportRequests) - exportsAfterNormalHost,
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static HostApplicationBuilder NewBuilder()
    => new(new HostApplicationBuilderSettings { ApplicationName = "Qyl.AddQylBehavior", DisableDefaults = true });

static int GetFreePort()
{
    var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}
