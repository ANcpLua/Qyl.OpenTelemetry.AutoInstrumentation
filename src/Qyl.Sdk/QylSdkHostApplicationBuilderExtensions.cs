using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.Hosting;

namespace Qyl;

/// <summary>
/// The one-line qyl onboarding surface: <c>builder.AddQyl()</c> activates the qyl
/// auto-instrumentation listeners, wires the OpenTelemetry SDK with qyl-owned ASP.NET Core and
/// HttpClient spans, version-pinned GenAI, Azure SDK, MCP, and CoreWCF sources plus the native and
/// qyl-owned meter inventory, propagates
/// <c>session.id</c> across traces, and exports traces, metrics, and logs over OTLP — to
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> when set, otherwise to a locally discovered qyl collector.
/// The specialist EF Core and SqlClient packages and the gRPC-client listener emit spans under
/// the single qyl ActivitySource, so they need no extra source registration here. Applications
/// still install the specialist package for each dependency-heavy database integration; adding
/// competing native sources would double-report the same operations.
/// </summary>
public static class QylSdkHostApplicationBuilderExtensions
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Activate qyl instrumentation, session propagation, and OTLP export.</summary>
    /// <remarks>
    /// Wrapper-based GenAI telemetry additionally requires opting the library itself in:
    /// <c>agent.AsBuilder().UseOpenTelemetry().Build()</c> for Microsoft.Agents.AI, or
    /// <c>chatClient.AsBuilder().UseOpenTelemetry().Build()</c> for a bare IChatClient. The
    /// Workflows use <c>WorkflowBuilder.WithOpenTelemetry()</c>. MCP and CoreWCF emit from
    /// their official SDK paths without a separate telemetry wrapper.
    /// </remarks>
    public static IHostApplicationBuilder AddQyl(
        this IHostApplicationBuilder builder,
        Action<QylSdkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new QylSdkOptions();
        configure?.Invoke(options);

        QylAutoInstrumentationBootstrap.Boot();
        builder.Services.AddQylAspNetCoreInstrumentation();

        // The exporter honors the standard OTLP environment variables on its own; discovery only
        // fills the gap when neither the app nor the environment configured an endpoint.
        var endpoint = options.CollectorEndpoint;
        if (endpoint is null
            && options.EnableCollectorDiscovery
            && string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable)))
        {
            endpoint = CollectorDiscovery.DiscoverEndpoint();
        }

        var serviceName = options.ServiceName
                          ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                          ?? builder.Environment.ApplicationName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .WithTracing(tracing =>
            {
                tracing.AddSource(QylTelemetrySources.GetEnabledActivitySourceNames());

                foreach (var source in options.AdditionalSources)
                    tracing.AddSource(source);

                if (QylTelemetrySources.IsAzureTracingEnabled())
                    tracing.AddProcessor(new QylAzureSpanProcessor());

                if (options.EnableSessionPropagation)
                    tracing.AddProcessor(new QylSessionSpanProcessor());

                tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, "/v1/traces"));
            });

        if (options.EnableMetricsExport)
        {
            builder.Services.AddOpenTelemetry().WithMetrics(metrics =>
            {
                // The full qyl meter inventory (ASP.NET Core, HttpClient, DNS, database,
                // messaging, runtime — honoring per-signal instrumentation options) plus the
                // GenAI meters, which are emitted by the same UseOpenTelemetry() opt-in that
                // produces the gen_ai spans.
                metrics.AddMeter(QylMetricMeters.GetEnabledMeterNames());
                metrics.AddMeter(QylTelemetrySources.GetEnabledMeterNames());

                foreach (var meter in options.AdditionalMeters)
                    metrics.AddMeter(meter);

                metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, "/v1/metrics"));
            });
        }

        if (options.EnableLogExport)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;
                logging.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, "/v1/logs"));
            });
        }

        return builder;
    }

    private static void ConfigureExporter(OtlpExporterOptions exporter, Uri? endpoint, string signalPath)
    {
        if (endpoint is null)
            return;

        // Port 4317 is the collector's gRPC listener; anything else speaks OTLP/HTTP. Unlike the
        // environment-variable path, a programmatic http/protobuf endpoint is used verbatim, so
        // the per-signal path is appended here.
        if (endpoint.Port == 4317)
        {
            exporter.Protocol = OtlpExportProtocol.Grpc;
            exporter.Endpoint = endpoint;
        }
        else
        {
            exporter.Protocol = OtlpExportProtocol.HttpProtobuf;
            exporter.Endpoint = new Uri(endpoint, signalPath);
        }
    }
}
