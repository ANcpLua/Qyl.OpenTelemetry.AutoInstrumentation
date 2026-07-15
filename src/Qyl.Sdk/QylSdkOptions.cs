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

    /// <summary>Export application logs over OTLP alongside traces. Defaults to true.</summary>
    public bool EnableLogExport { get; set; } = true;

    /// <summary>
    /// Register a MeterProvider covering the qyl auto-instrumentation meter inventory (ASP.NET
    /// Core, HttpClient, DNS, database, messaging, runtime) plus the GenAI meters, and export it
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
    /// qyl defaults (qyl listeners, ASP.NET Core, HttpClient, and the GenAI sources).
    /// </summary>
    public IList<string> AdditionalSources { get; } = [];

    /// <summary>
    /// Additional <see cref="System.Diagnostics.Metrics.Meter"/> names to subscribe beyond the
    /// qyl defaults (the auto-instrumentation meter inventory and the GenAI meters).
    /// </summary>
    public IList<string> AdditionalMeters { get; } = [];
}
