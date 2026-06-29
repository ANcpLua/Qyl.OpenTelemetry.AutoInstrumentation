using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Qyl.OpenTelemetry.AutoInstrumentation.Publishing;

/// <summary>
/// Registers TCG publishing: emits this binary's Telemetry Capability Graph as a single
/// OpenTelemetry LogRecord at host startup (North Star pillar 3 — the open exchange channel).
/// </summary>
public static class QylTelemetryCapabilityGraphPublishingExtensions
{
    /// <summary>
    /// Publish the binary's Telemetry Capability Graph (TCG) once at host startup as an
    /// <c>Information</c> log with event name <c>qyl.telemetry_capability_graph</c>: the TCG JSON is the
    /// log body and <c>qyl.tcg.schema_version</c> / <c>qyl.tcg.capability_count</c> are attributes (see
    /// <c>docs/TELEMETRY_CAPABILITY_GRAPH.md</c> → publication channel 2). When the app has OpenTelemetry
    /// logging with an OTLP exporter configured, this becomes a true OTLP LogRecord. The exporter stays
    /// app-owned — this package emits through <see cref="ILogger"/> and takes no OpenTelemetry SDK dependency.
    /// </summary>
    /// <param name="services">The service collection to register the publisher on.</param>
    /// <returns>The same <paramref name="services"/> instance, for chaining.</returns>
    public static IServiceCollection AddQylTelemetryCapabilityGraphPublisher(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.AddHostedService<TelemetryCapabilityGraphPublisher>();
        return services;
    }
}

/// <summary>
/// Emits the binary's Telemetry Capability Graph exactly once, when the host starts. AOT/trim-clean:
/// the log state is a fixed attribute array and the body is a constant string — no reflection.
/// </summary>
internal sealed class TelemetryCapabilityGraphPublisher : IHostedService
{
    private const string EventName = "qyl.telemetry_capability_graph";
    private static readonly EventId PublishEvent = new(0, EventName);

    private readonly ILogger<TelemetryCapabilityGraphPublisher> _logger;

    public TelemetryCapabilityGraphPublisher(ILogger<TelemetryCapabilityGraphPublisher> logger)
        => _logger = logger;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (cancellationToken.IsCancellationRequested)
            return Task.FromCanceled(cancellationToken);

        var attributes = new[]
        {
            new KeyValuePair<string, object?>("qyl.tcg.schema_version", QylTelemetryCapabilityGraph.SchemaVersion),
            new KeyValuePair<string, object?>("qyl.tcg.capability_count", QylTelemetryCapabilityGraph.CapabilityCount),
        };

        // Body = the TCG JSON; attributes = schema version + capability count; event name above. The
        // OpenTelemetry logging bridge maps the state's key/value pairs to LogRecord attributes and the
        // formatter output to the LogRecord body.
        _logger.Log(
            LogLevel.Information,
            PublishEvent,
            attributes,
            exception: null,
            formatter: static (_, _) => QylTelemetryCapabilityGraph.Json);

        return Task.CompletedTask;
    }

    public Task StopAsync(CancellationToken cancellationToken) => Task.CompletedTask;
}
