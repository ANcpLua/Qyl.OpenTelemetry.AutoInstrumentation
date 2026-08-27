using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Incubating.Names.QylTelemetryNames;

namespace Qyl.Telemetry.AutoInstrumentation;

internal static class QylMetricMeters
{
    /// <summary>Well-known ASP.NET Core Hosting Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";
    /// <summary>Well-known ASP.NET Core Routing Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";
    /// <summary>Well-known ASP.NET Core Diagnostics Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";
    /// <summary>Well-known ASP.NET Core Rate Limiting Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";
    /// <summary>Well-known ASP.NET Core Header Parsing Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";
    /// <summary>Well-known ASP.NET Core Kestrel Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";
    /// <summary>Well-known ASP.NET Core SignalR HTTP Connections Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";
    /// <summary>Well-known ASP.NET Core Authorization Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";
    /// <summary>Well-known ASP.NET Core Authentication Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";
    /// <summary>Well-known ASP.NET Core Components Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    /// <summary>Well-known ASP.NET Core Components Lifecycle Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";
    /// <summary>Well-known ASP.NET Core Components Server Circuits Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";
    /// <summary>Well-known HTTP Client Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string HttpClientMeterName = "System.Net.Http";
    /// <summary>Well-known System.Net DNS name resolution Meter Name value used by qyl auto-instrumentation.</summary>
    internal const string NameResolutionMeterName = "System.Net.NameResolution";
    /// <summary>The qyl database meter (<c>db.client.operation.duration</c>).</summary>
    internal const string DatabaseMeterName = QylTelemetryNames.Scopes.QylOpenTelemetryAutoInstrumentationDatabase;
    /// <summary>Npgsql's library-native Meter, carrying its connection-pool and command instruments.</summary>
    internal const string NpgsqlNativeMeterName = "Npgsql";
    /// <summary>The qyl NServiceBus meter (<c>nservicebus.messaging.operation.duration</c>).</summary>
    internal const string NServiceBusMeterName = QylTelemetryNames.Scopes.QylOpenTelemetryAutoInstrumentationNServiceBus;
    /// <summary>NServiceBus's library-native core Meter.</summary>
    internal const string NServiceBusNativeMeterName = "NServiceBus.Core";
    /// <summary>NServiceBus's library-native incoming-pipeline Meter.</summary>
    internal const string NServiceBusNativeIncomingPipelineMeterName = "NServiceBus.Core.Pipeline.Incoming";
    /// <summary>The runtime's built-in meter (GC, JIT, thread pool, exceptions, process CPU and memory). qyl subscribes to it and produces nothing on it.</summary>
    internal const string RuntimeMeterName = QylTelemetryNames.Scopes.SystemRuntime;

    internal static string[] GetEnabledMeterNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(20);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.AspNetCore))
        {
            names.Add(AspNetCoreHostingMeterName);
            names.Add(AspNetCoreRoutingMeterName);
            names.Add(AspNetCoreDiagnosticsMeterName);
            names.Add(AspNetCoreRateLimitingMeterName);
            names.Add(AspNetCoreHeaderParsingMeterName);
            names.Add(AspNetCoreServerKestrelMeterName);
            names.Add(AspNetCoreHttpConnectionsMeterName);
            names.Add(AspNetCoreAuthorizationMeterName);
            names.Add(AspNetCoreAuthenticationMeterName);
            names.Add(AspNetCoreComponentsMeterName);
            names.Add(AspNetCoreComponentsLifecycleMeterName);
            names.Add(AspNetCoreComponentsServerCircuitsMeterName);
        }

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
        {
            names.Add(HttpClientMeterName);
            names.Add(NameResolutionMeterName);
        }

        var databaseMeterEnabled = false;
        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Npgsql))
            databaseMeterEnabled = true;

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient))
            databaseMeterEnabled = true;

        if (databaseMeterEnabled)
            names.Add(DatabaseMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus))
            names.Add(NServiceBusMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NetRuntime) ||
            options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Process))
            names.Add(RuntimeMeterName);

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
