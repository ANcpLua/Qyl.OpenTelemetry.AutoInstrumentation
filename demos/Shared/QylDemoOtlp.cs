using OpenTelemetry;
using OpenTelemetry.Exporter;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

/// <summary>
/// The OTLP pipeline of a demo that wires the OpenTelemetry SDK itself instead of calling AddQyl. It
/// exports to the collector <c>QYL_ENDPOINT</c> names, by the rule AddQyl applies to the same
/// variable: port 4317 speaks OTLP/gRPC at the URL as given, every other port OTLP/HTTP with the
/// signal's path appended. Without the variable no provider is built and the demo asserts on its own
/// listeners alone. The Demos target points the variable at the local qyl collector and the LiveCheck
/// target at weaver's listener.
/// </summary>
internal static class QylDemoOtlp
{
    internal const string EndpointVariable = "QYL_ENDPOINT";

    private const int GrpcPort = 4317;

    internal static Uri? Endpoint =>
        Environment.GetEnvironmentVariable(EndpointVariable) is { Length: > 0 } value
            ? new Uri(value, UriKind.Absolute)
            : null;

    /// <summary>A tracer provider exporting the named sources, or null when no endpoint is configured.</summary>
    internal static TracerProvider? CreateTracerProvider(string serviceName, params string[] sources)
    {
        if (Endpoint is not { } endpoint)
            return null;

        return Sdk.CreateTracerProviderBuilder()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .AddSource(sources)
            .AddOtlpExporter(exporter => Configure(exporter, endpoint, "/v1/traces"))
            .Build();
    }

    /// <summary>A meter provider exporting the named meters, or null when no endpoint is configured.</summary>
    internal static MeterProvider? CreateMeterProvider(string serviceName, params string[] meters)
    {
        if (Endpoint is not { } endpoint)
            return null;

        return Sdk.CreateMeterProviderBuilder()
            .ConfigureResource(resource => resource.AddService(serviceName))
            .AddMeter(meters)
            .AddOtlpExporter(exporter => Configure(exporter, endpoint, "/v1/metrics"))
            .Build();
    }

    /// <summary>Exports everything the provider holds before the demo reports; a no-op without a provider.</summary>
    internal static void Flush(TracerProvider? provider) => provider?.ForceFlush(10_000);

    /// <summary>Collects and exports every meter the provider holds; a no-op without a provider.</summary>
    internal static void Flush(MeterProvider? provider) => provider?.ForceFlush(10_000);

    private static void Configure(OtlpExporterOptions exporter, Uri endpoint, string signalPath)
    {
        if (endpoint.Port == GrpcPort)
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
