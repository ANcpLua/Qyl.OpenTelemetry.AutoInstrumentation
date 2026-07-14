using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Qyl.OpenTelemetry.AutoInstrumentation;
using Qyl.OpenTelemetry.AutoInstrumentation.Hosting;

namespace Qyl;

/// <summary>
/// The one-line qyl onboarding surface: <c>builder.AddQyl()</c> activates the qyl
/// auto-instrumentation listeners, wires the OpenTelemetry SDK with the qyl, ASP.NET Core,
/// HttpClient, and GenAI sources, propagates <c>session.id</c> across traces, and exports over
/// OTLP — to <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> when set, otherwise to a locally discovered qyl
/// collector.
/// </summary>
public static class QylSdkHostApplicationBuilderExtensions
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    /// <summary>Activate qyl instrumentation, session propagation, and OTLP export.</summary>
    /// <remarks>
    /// GenAI telemetry additionally requires opting the agent itself in:
    /// <c>agent.AsBuilder().UseOpenTelemetry().Build()</c> for Microsoft.Agents.AI, or
    /// <c>chatClient.AsBuilder().UseOpenTelemetry().Build()</c> for a bare IChatClient. The
    /// sources are pre-registered here so that single line is all that's left.
    /// </remarks>
    public static IHostApplicationBuilder AddQyl(
        this IHostApplicationBuilder builder,
        Action<QylSdkOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);

        var options = new QylSdkOptions();
        configure?.Invoke(options);

        QylAutoInstrumentationBootstrap.Boot();

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
                tracing.AddSource(
                    QylActivitySource.Name,
                    "Microsoft.AspNetCore",
                    "System.Net.Http",
                    "Experimental.Microsoft.Agents.AI",
                    "Experimental.Microsoft.Extensions.AI");

                foreach (var source in options.AdditionalSources)
                    tracing.AddSource(source);

                if (options.EnableSessionPropagation)
                    tracing.AddProcessor(new QylSessionSpanProcessor());

                tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, endpoint, "/v1/traces"));
            });

        if (options.EnableLogExport)
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
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
