using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

namespace Qyl.Telemetry.AutoInstrumentation;

internal static class QylMetricMeters
{
    internal const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";
    internal const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";
    internal const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";
    internal const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";
    internal const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";
    internal const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";
    internal const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";
    internal const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";
    internal const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";
    internal const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    internal const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";
    internal const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";
    internal const string HttpClientMeterName = "System.Net.Http";
    internal const string NameResolutionMeterName = "System.Net.NameResolution";
    /// <summary>The qyl database meter (<c>db.client.operation.duration</c>).</summary>
    internal const string DatabaseMeterName = QylTelemetryNames.Scopes.QylTelemetryAutoInstrumentationDatabase;
    /// <summary>Npgsql's library-native Meter, carrying its connection-pool and command instruments.</summary>
    internal const string NpgsqlNativeMeterName = "Npgsql";
    /// <summary>The qyl NServiceBus meter (<c>nservicebus.messaging.operation.duration</c>).</summary>
    internal const string NServiceBusMeterName = QylTelemetryNames.Scopes.QylTelemetryAutoInstrumentationNServiceBus;
    /// <summary>NServiceBus's library-native core Meter.</summary>
    internal const string NServiceBusNativeMeterName = "NServiceBus.Core";
    /// <summary>NServiceBus's library-native incoming-pipeline Meter.</summary>
    internal const string NServiceBusNativeIncomingPipelineMeterName = "NServiceBus.Core.Pipeline.Incoming";
    /// <summary>The runtime's built-in meter (GC, JIT, thread pool, exceptions, process CPU and memory). qyl subscribes to it and produces nothing on it.</summary>
    internal const string RuntimeMeterName = QylTelemetryNames.Scopes.SystemRuntime;

    /// <summary>
    /// The meter names each instrumentation id contributes, in registration order. Two ids may name
    /// the same meter — Npgsql and SqlClient both produce on the qyl database meter, NetRuntime and
    /// Process both subscribe to the runtime meter — and the walk keeps the first occurrence, so
    /// enabling either yields exactly one entry.
    /// </summary>
    private static readonly (string InstrumentationId, string[] MeterNames)[] MetricMeterTable =
    [
        (QylAutoInstrumentationIds.AspNetCore,
        [
            AspNetCoreHostingMeterName,
            AspNetCoreRoutingMeterName,
            AspNetCoreDiagnosticsMeterName,
            AspNetCoreRateLimitingMeterName,
            AspNetCoreHeaderParsingMeterName,
            AspNetCoreServerKestrelMeterName,
            AspNetCoreHttpConnectionsMeterName,
            AspNetCoreAuthorizationMeterName,
            AspNetCoreAuthenticationMeterName,
            AspNetCoreComponentsMeterName,
            AspNetCoreComponentsLifecycleMeterName,
            AspNetCoreComponentsServerCircuitsMeterName,
        ]),
        (QylAutoInstrumentationIds.HttpClient, [HttpClientMeterName, NameResolutionMeterName]),
        (QylAutoInstrumentationIds.Npgsql, [DatabaseMeterName]),
        (QylAutoInstrumentationIds.SqlClient, [DatabaseMeterName]),
        (QylAutoInstrumentationIds.NServiceBus, [NServiceBusMeterName]),
        (QylAutoInstrumentationIds.NetRuntime, [RuntimeMeterName]),
        (QylAutoInstrumentationIds.Process, [RuntimeMeterName]),
    ];

    internal static string[] GetEnabledMeterNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>();

        foreach (var (instrumentationId, meterNames) in MetricMeterTable)
        {
            if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, instrumentationId))
                AddDistinct(names, meterNames);
        }

        if (options.MetricsEnabled)
            AddDistinct(names, options.AdditionalMetricMeterNames);

        return [.. names];
    }

    private static void AddDistinct(List<string> target, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (!target.Contains(name, StringComparer.Ordinal))
                target.Add(name);
        }
    }
}
