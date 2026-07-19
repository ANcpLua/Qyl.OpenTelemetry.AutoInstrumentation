using System.Diagnostics.Metrics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Metrics;
using Qyl.OpenTelemetry.AutoInstrumentation;

var exportedMetrics = new List<Metric>();

using var meterProvider = Sdk
    .CreateMeterProviderBuilder()
    .AddMeter(DemoMetricNames.AspNetCore)
    .AddInMemoryExporter(exportedMetrics)
    .Build();

var services = new ServiceCollection();
services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
services.AddSingleton<IWebHostEnvironment>(new TestWebHostEnvironment());
services.AddLogging();
services.AddRazorComponents();

await using var serviceProvider = services.BuildServiceProvider();
var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();
await using var renderer = new HtmlRenderer(serviceProvider, loggerFactory);
await renderer.Dispatcher.InvokeAsync(async () =>
{
    _ = await renderer.RenderComponentAsync<TestComponent>();
});

await RunAspNetCoreRequestAsync(args);

for (var attempt = 0; attempt < 20 && exportedMetrics.Count is 0; attempt++)
{
    meterProvider.ForceFlush();
    await Task.Delay(TimeSpan.FromMilliseconds(100));
}

meterProvider.ForceFlush();

var report = AspNetCoreMetricsReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    DemoMetricNames.AspNetCore,
    exportedMetrics.Select(CapturedMetric.From).ToArray());

var json = JsonSerializer.Serialize(report, RealAspNetCoreMetricsJsonContext.Default.AspNetCoreMetricsReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task RunAspNetCoreRequestAsync(string[] args)
{
    var builder = WebApplication.CreateSlimBuilder(args);
    builder.WebHost.UseUrls("http://127.0.0.1:0");

    await using var app = builder.Build();
    app.Run(static context =>
    {
        context.Response.ContentType = "text/plain";
        return context.Response.WriteAsync("42");
    });

    await app.StartAsync();
    try
    {
        var address = app.Urls.FirstOrDefault()
            ?? throw new InvalidOperationException("ASP.NET Core metrics demo did not bind to an address.");

        using var httpClient = new HttpClient();
        using var response = await httpClient.GetAsync($"{address}/metrics/42");
        response.EnsureSuccessStatusCode();
    }
    finally
    {
        await app.StopAsync();
    }
}

internal sealed class TestComponent : ComponentBase
{
    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "p");
        builder.AddContent(1, "hello");
        builder.CloseElement();
    }
}

internal sealed class TestWebHostEnvironment : IWebHostEnvironment
{
    public string ApplicationName { get; set; } = "Qyl.RealAspNetCoreMetricsDemo";
    public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
    public string WebRootPath { get; set; } = AppContext.BaseDirectory;
    public string EnvironmentName { get; set; } = Environments.Development;
    public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}

internal sealed record CapturedMetric(
    string MeterName,
    string Name,
    int PointCount)
{
    public static CapturedMetric From(Metric metric)
        => new(
            metric.MeterName,
            metric.Name,
            CountMetricPoints(metric));

    private static int CountMetricPoints(Metric metric)
    {
        var count = 0;
        foreach (ref readonly var _ in metric.GetMetricPoints())
            count++;

        return count;
    }
}

internal sealed record AspNetCoreMetricsReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    string[] EnabledMeterNames,
    CapturedMetric[] Metrics)
{
    public static AspNetCoreMetricsReport Create(string runtimeMode, string[] enabledMeterNames, CapturedMetric[] metrics)
    {
        var failures = new List<string>();
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreHosting, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreRouting, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreDiagnostics, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreRateLimiting, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreHeaderParsing, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreServerKestrel, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreHttpConnections, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreAuthorization, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreAuthentication, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreComponents, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreComponentsLifecycle, failures);
        RequireEnabledMeter(enabledMeterNames, DemoMetricNames.AspNetCoreComponentsServerCircuits, failures);

        var capturedAspNetCoreMetrics = metrics
            .Where(static metric => metric.MeterName.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal))
            .ToArray();
        var componentMetrics = capturedAspNetCoreMetrics
            .Where(static metric => metric.MeterName.StartsWith("Microsoft.AspNetCore.Components", StringComparison.Ordinal))
            .ToArray();
        var hostingMetrics = capturedAspNetCoreMetrics
            .Where(static metric => StringComparer.Ordinal.Equals(metric.MeterName, DemoMetricNames.AspNetCoreHosting))
            .ToArray();

        if (componentMetrics.Length is 0)
        {
            failures.Add("expected real ASP.NET Core component metrics, got none");
            failures.Add("observed metrics: " + string.Join("|", metrics.Select(static metric => metric.MeterName + ":" + metric.Name).OrderBy(static name => name, StringComparer.Ordinal)));
        }

        if (hostingMetrics.Length is 0)
        {
            failures.Add("expected real ASP.NET Core hosting metrics, got none");
            failures.Add("observed metrics: " + string.Join("|", metrics.Select(static metric => metric.MeterName + ":" + metric.Name).OrderBy(static name => name, StringComparer.Ordinal)));
        }

        RequireMetric(componentMetrics, DemoMetricNames.AspNetCoreComponentsLifecycle, "aspnetcore.components.render_diff.duration", failures);
        RequireMetric(componentMetrics, DemoMetricNames.AspNetCoreComponentsLifecycle, "aspnetcore.components.render_diff.size", failures);
        RequireMetric(componentMetrics, DemoMetricNames.AspNetCoreComponentsLifecycle, "aspnetcore.components.update_parameters.duration", failures);
        RequireMetric(hostingMetrics, DemoMetricNames.AspNetCoreHosting, "http.server.request.duration", failures);

        foreach (var metric in capturedAspNetCoreMetrics.Where(static metric => metric.PointCount <= 0))
            failures.Add($"expected at least one metric point for {metric.MeterName}:{metric.Name}, got {metric.PointCount.ToString(CultureInfo.InvariantCulture)}");

        return new AspNetCoreMetricsReport(runtimeMode, failures.Count is 0, failures.ToArray(), enabledMeterNames, capturedAspNetCoreMetrics);
    }

    private static void RequireEnabledMeter(IEnumerable<string> names, string expected, ICollection<string> failures)
    {
        if (!names.Any(name => StringComparer.Ordinal.Equals(name, expected)))
            failures.Add($"missing enabled ASP.NET Core meter {expected}");
    }

    private static void RequireMetric(IEnumerable<CapturedMetric> metrics, string meterName, string metricName, ICollection<string> failures)
    {
        if (!metrics.Any(metric => StringComparer.Ordinal.Equals(metric.MeterName, meterName) && StringComparer.Ordinal.Equals(metric.Name, metricName)))
            failures.Add($"missing ASP.NET Core metric {meterName}:{metricName}");
    }
}

internal static class DemoMetricNames
{
    internal const string AspNetCoreHosting = "Microsoft.AspNetCore.Hosting";
    internal const string AspNetCoreRouting = "Microsoft.AspNetCore.Routing";
    internal const string AspNetCoreDiagnostics = "Microsoft.AspNetCore.Diagnostics";
    internal const string AspNetCoreRateLimiting = "Microsoft.AspNetCore.RateLimiting";
    internal const string AspNetCoreHeaderParsing = "Microsoft.AspNetCore.HeaderParsing";
    internal const string AspNetCoreServerKestrel = "Microsoft.AspNetCore.Server.Kestrel";
    internal const string AspNetCoreHttpConnections = "Microsoft.AspNetCore.Http.Connections";
    internal const string AspNetCoreAuthorization = "Microsoft.AspNetCore.Authorization";
    internal const string AspNetCoreAuthentication = "Microsoft.AspNetCore.Authentication";
    internal const string AspNetCoreComponents = "Microsoft.AspNetCore.Components";
    internal const string AspNetCoreComponentsLifecycle = "Microsoft.AspNetCore.Components.Lifecycle";
    internal const string AspNetCoreComponentsServerCircuits = "Microsoft.AspNetCore.Components.Server.Circuits";

    internal static readonly string[] AspNetCore =
    [
        AspNetCoreHosting,
        AspNetCoreRouting,
        AspNetCoreDiagnostics,
        AspNetCoreRateLimiting,
        AspNetCoreHeaderParsing,
        AspNetCoreServerKestrel,
        AspNetCoreHttpConnections,
        AspNetCoreAuthorization,
        AspNetCoreAuthentication,
        AspNetCoreComponents,
        AspNetCoreComponentsLifecycle,
        AspNetCoreComponentsServerCircuits,
    ];
}

[JsonSerializable(typeof(AspNetCoreMetricsReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealAspNetCoreMetricsJsonContext : JsonSerializerContext;
