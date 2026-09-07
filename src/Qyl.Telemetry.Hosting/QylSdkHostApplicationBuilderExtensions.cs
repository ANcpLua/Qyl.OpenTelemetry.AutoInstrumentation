using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.AutoInstrumentation.Hosting;

namespace Qyl;

/// <summary>
/// The one-line qyl onboarding surface: <c>builder.AddQyl()</c> activates the qyl
/// auto-instrumentation bootstrap, wires the OpenTelemetry SDK with ASP.NET Core's own server span
/// (enriched by the qyl middleware, never duplicated), every row of the native-source table — the
/// BCL's outbound <c>System.Net.Http</c> span among them — plus the native and
/// qyl-owned meter inventory, carries
/// <c>session.id</c> from a span to its in-process descendants, and exports traces, metrics, and
/// logs over OTLP — to
/// <c>OTEL_EXPORTER_OTLP_ENDPOINT</c> when set, otherwise to a locally discovered qyl collector.
/// <c>session.id</c> is a span tag and never baggage, so it does not reach the next process on its
/// own. The specialist EF Core and SqlClient packages emit spans under the single qyl
/// ActivitySource, so they need no extra source registration here. Applications still install the
/// specialist package for each dependency-heavy database integration; adding competing native
/// sources would double-report the same operations.
/// <para>
/// The method is idempotent: a second call — a composing library and the application both asking
/// for it — returns the builder untouched, so the pipeline is built once. The first call's options
/// win.
/// </para>
/// </summary>
public static class QylSdkHostApplicationBuilderExtensions
{
    private const string OtlpEndpointVariable = "OTEL_EXPORTER_OTLP_ENDPOINT";

    // Four 100 ms connect attempts plus a DNS lookup; the probe runs while the host wires itself, so
    // this bound is only reached when the SDK builds its pipeline immediately after AddQyl.
    private static readonly TimeSpan DiscoveryTimeout = TimeSpan.FromMilliseconds(600);

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

        // Idempotent, like the instrumentation registration nested below. Each call queues its own
        // WithTracing callback against the one TracerProvider, so a second one would add a second
        // session processor, a second native processor and a second OTLP exporter — every span,
        // metric and log exported twice, silently. That is not a hypothetical: an application that
        // follows the README's builder.AddQyl() and also calls AddQylApi, which calls AddQyl itself,
        // does exactly this. The first call wins, including its options.
        if (builder.Services.Any(static service => service.ServiceType == typeof(QylSdkRegistration)))
            return builder;

        builder.Services.AddSingleton<QylSdkRegistration>();

        var options = new QylSdkOptions();
        configure?.Invoke(options);

        QylAutoInstrumentationBootstrap.Boot();
        builder.Services.AddQylAspNetCoreInstrumentation();

        // The exporter honors the standard OTLP environment variables on its own; discovery only
        // fills the gap when neither the app nor the environment configured an endpoint.
        var endpointFromEnvironment =
            !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(OtlpEndpointVariable));
        var endpoint = options.CollectorEndpoint;
        var probe = endpoint is null && options.EnableCollectorDiscovery && !endpointFromEnvironment
            ? CollectorDiscovery.Start()
            : null;

        // RequireConfiguredEndpoint is the one caller that needs the answer now: it means "no
        // endpoint, no export", and the exporter is either registered or it is not. Everyone else
        // gets the probe's answer in the exporter's options callback, which the SDK invokes when it
        // builds the pipeline — so AddQyl returns without touching a socket.
        if (probe is not null && options.RequireConfiguredEndpoint)
            endpoint = CollectorDiscovery.WaitForEndpoint(probe, DiscoveryTimeout);

        // A null endpoint is not the same as "no destination": the exporter still reads the standard
        // environment variables, and failing that falls back to its own localhost default. Only a
        // caller that is itself a telemetry destination cares about the difference, and for it that
        // fallback points at its own ingest port — so it opts out of exporting entirely.
        var exportEnabled = !options.RequireConfiguredEndpoint || endpoint is not null || endpointFromEnvironment;
        var resolvedEndpoint = endpoint;
        Uri? ResolveEndpoint()
            => resolvedEndpoint ??= probe is null ? null : CollectorDiscovery.WaitForEndpoint(probe, DiscoveryTimeout);

        var serviceName = options.ServiceName
                          ?? Environment.GetEnvironmentVariable("OTEL_SERVICE_NAME")
                          ?? builder.Environment.ApplicationName;

        builder.Services.AddOpenTelemetry()
            .ConfigureResource(resource =>
            {
                resource.AddService(serviceName, serviceVersion: options.ServiceVersion);

                if (options.ResourceAttributes.Count > 0)
                    resource.AddAttributes(options.ResourceAttributes);
            })
            .WithTracing(tracing =>
            {
                var subscribedSources = QylTelemetrySources.GetEnabledActivitySourceNames();
                tracing.AddSource(subscribedSources);

                // A consumer's AdditionalSources are deduplicated against what qyl already
                // subscribed. Naming a source qyl owns — "System.Net.Http" is the one a consumer
                // reaches for — must not register it twice.
                var additionalSources = new HashSet<string>(subscribedSources, StringComparer.Ordinal);
                foreach (var source in options.AdditionalSources)
                {
                    if (additionalSources.Add(source))
                        tracing.AddSource(source);
                }

                var nativeSourceRows = QylTelemetrySources.GetEnabledNativeSourceRows();
                if (nativeSourceRows.Length > 0)
                    tracing.AddProcessor(new QylNativeSpanProcessor(nativeSourceRows));

                if (options.EnableSessionPropagation)
                    tracing.AddProcessor(new QylSessionSpanProcessor());

                options.ConfigureTracing?.Invoke(tracing);

                if (exportEnabled)
                    tracing.AddOtlpExporter(exporter => ConfigureExporter(exporter, ResolveEndpoint(), "/v1/traces"));
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

                var additionalMeters = new HashSet<string>(
                    QylMetricMeters.GetEnabledMeterNames().Concat(QylTelemetrySources.GetEnabledMeterNames()),
                    StringComparer.Ordinal);
                foreach (var meter in options.AdditionalMeters)
                {
                    if (additionalMeters.Add(meter))
                        metrics.AddMeter(meter);
                }

                if (exportEnabled)
                    metrics.AddOtlpExporter(exporter => ConfigureExporter(exporter, ResolveEndpoint(), "/v1/metrics"));
            });
        }

        // OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED is the upstream global logs control; the
        // OpenTelemetry ILogger provider is the only thing qyl binds it to, so it gates registration.
        if (options.EnableLogExport && QylTelemetrySources.IsLogRecordCaptureEnabled())
        {
            builder.Logging.AddOpenTelemetry(logging =>
            {
                logging.IncludeFormattedMessage = true;
                logging.IncludeScopes = true;

                if (exportEnabled)
                    logging.AddOtlpExporter(exporter => ConfigureExporter(exporter, ResolveEndpoint(), "/v1/logs"));
            });
        }

        return builder;
    }

    /// <summary>Marks the container as already carrying a qyl registration.</summary>
    private sealed class QylSdkRegistration;

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
