using OpenTelemetry.Trace;

namespace Qyl;

/// <summary>
/// Options for <see cref="QylSdkHostApplicationBuilderExtensions.AddQyl"/>. Every property has a
/// zero-config default; the options exist for the cases where the conventions don't fit.
/// </summary>
public sealed class QylSdkOptions
{
    /// <summary>
    /// Logical service name stamped on the OpenTelemetry resource. Defaults to
    /// <c>OTEL_SERVICE_NAME</c> when set, otherwise the host application name.
    /// </summary>
    public string? ServiceName { get; set; }

    /// <summary>
    /// Version stamped on the OpenTelemetry resource as <c>service.version</c>. Leave null to omit
    /// it. Setting it makes a telemetry regression attributable to a specific build, which is the
    /// whole point of the attribute — a deployment that cannot name its own version cannot be
    /// bisected.
    /// </summary>
    public string? ServiceVersion { get; set; }

    /// <summary>
    /// Extra attributes merged onto the OpenTelemetry resource, for facts that describe the process
    /// rather than any one span — schema URL, deployment environment, enabled capabilities.
    /// </summary>
    public IList<KeyValuePair<string, object>> ResourceAttributes { get; } = [];

    /// <summary>
    /// Escape hatch for tracing configuration the typed properties do not cover — extra processors,
    /// samplers, or instrumentation. Runs after the qyl sources and processors are registered and
    /// before the OTLP exporter, so a processor added here observes spans on their way out.
    /// </summary>
    public Action<TracerProviderBuilder>? ConfigureTracing { get; set; }

    /// <summary>
    /// Explicit collector endpoint. When null, the standard <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>
    /// environment variable wins if present; otherwise local discovery probes for a qyl collector
    /// (see <see cref="EnableCollectorDiscovery"/>).
    /// </summary>
    public Uri? CollectorEndpoint { get; set; }

    /// <summary>
    /// Probe localhost (and the <c>qyl</c> container-network host) for a running collector when no
    /// endpoint is configured. Defaults to true.
    /// </summary>
    public bool EnableCollectorDiscovery { get; set; } = true;

    /// <summary>
    /// Wire the OTLP exporters only when an endpoint is actually configured — explicitly via
    /// <see cref="CollectorEndpoint"/>, through <c>OTEL_EXPORTER_OTLP_ENDPOINT</c>, or by discovery
    /// — instead of falling back to the exporter's own <c>localhost</c> default. Defaults to false,
    /// which suits an ordinary application: the built-in default is the local qyl collector.
    /// <para>
    /// A process that is <em>itself</em> a telemetry destination must set this to true. For the
    /// collector, the exporter's default port is its own ingest port, so an unconfigured export
    /// would feed its output back into its input. Setting this makes "no endpoint" mean "do not
    /// export" rather than "export to whatever is listening locally".
    /// </para>
    /// </summary>
    public bool RequireConfiguredEndpoint { get; set; }

    /// <summary>Export application logs over OTLP alongside traces. Defaults to true.</summary>
    public bool EnableLogExport { get; set; } = true;

    /// <summary>
    /// Register a MeterProvider covering the native and qyl-owned auto-instrumentation meter
    /// inventory (ASP.NET Core, HttpClient, DNS, database, messaging, runtime) plus the GenAI meters, and export it
    /// over OTLP alongside traces. Defaults to true.
    /// </summary>
    public bool EnableMetricsExport { get; set; } = true;

    /// <summary>
    /// Copy a <c>session.id</c> tag from the nearest in-process ancestor span onto spans that lack
    /// one, so stamping a single request-level span groups its whole trace into a qyl session.
    /// Defaults to true.
    /// </summary>
    public bool EnableSessionPropagation { get; set; } = true;

    /// <summary>
    /// Additional <see cref="System.Diagnostics.ActivitySource"/> names to subscribe beyond the
    /// qyl defaults (qyl listener spans plus version-pinned GenAI, Azure SDK, MCP, and CoreWCF
    /// sources).
    /// </summary>
    public IList<string> AdditionalSources { get; } = [];

    /// <summary>
    /// Additional <see cref="System.Diagnostics.Metrics.Meter"/> names to subscribe beyond the
    /// qyl defaults (the auto-instrumentation meter inventory and the GenAI meters).
    /// </summary>
    public IList<string> AdditionalMeters { get; } = [];
}
