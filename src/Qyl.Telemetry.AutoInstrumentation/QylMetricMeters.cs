using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

namespace Qyl.Telemetry.AutoInstrumentation;

internal static class QylMetricMeters
{
    private const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";
    private const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";
    private const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";
    private const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";
    private const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";
    private const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";
    private const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";
    private const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";
    private const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";
    private const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    private const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";
    private const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";
    private const string HttpClientMeterName = "System.Net.Http";
    private const string NameResolutionMeterName = "System.Net.NameResolution";
    /// <summary>The qyl database meter (<c>db.client.operation.duration</c>).</summary>
    internal const string DatabaseMeterName = QylTelemetryNames.Scopes.QylTelemetryAutoInstrumentationDatabase;
    /// <summary>The runtime's built-in meter (GC, JIT, thread pool, exceptions, process CPU and memory). qyl subscribes to it and produces nothing on it.</summary>
    private const string RuntimeMeterName = QylTelemetryNames.Scopes.SystemRuntime;
    /// <summary>Quartz 4's library-native meter, published as QuartzInstrumentation.MeterName.</summary>
    private const string QuartzNativeMeterName = "Quartz";

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
        (QylAutoInstrumentationIds.NetRuntime, [RuntimeMeterName]),
        (QylAutoInstrumentationIds.Process, [RuntimeMeterName]),
        (QylAutoInstrumentationIds.Quartz, [QuartzNativeMeterName]),
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
