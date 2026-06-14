namespace Qyl.AutoInstrumentation;

/// <summary>
/// Meter names required by the metrics side of the auto-instrumentation contract.
/// These are registration targets for source-visible <c>MeterProviderBuilder.AddMeter(...)</c>
/// interception; they do not create instruments by themselves.
/// </summary>
public static class QylMetricMeters
{
    /// <summary>Well-known ASP.NET Core Hosting Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";
    /// <summary>Well-known ASP.NET Core Routing Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";
    /// <summary>Well-known ASP.NET Core Diagnostics Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";
    /// <summary>Well-known ASP.NET Core Rate Limiting Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";
    /// <summary>Well-known ASP.NET Core Header Parsing Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";
    /// <summary>Well-known ASP.NET Core Kestrel Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";
    /// <summary>Well-known ASP.NET Core SignalR HTTP Connections Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";
    /// <summary>Well-known ASP.NET Core Authorization Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";
    /// <summary>Well-known ASP.NET Core Authentication Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";
    /// <summary>Well-known ASP.NET Core Components Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    /// <summary>Well-known ASP.NET Core Components Lifecycle Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";
    /// <summary>Well-known ASP.NET Core Components Server Circuits Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";
    /// <summary>Well-known HTTP Client Meter Name value used by qyl auto-instrumentation.</summary>
    public const string HttpClientMeterName = "System.Net.Http";
    /// <summary>Well-known System.Net DNS name resolution Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NameResolutionMeterName = "System.Net.NameResolution";
    /// <summary>Well-known Database Meter Name value used by qyl auto-instrumentation.</summary>
    public const string DatabaseMeterName = "Qyl.AutoInstrumentation.Database";
    /// <summary>Well-known Npgsql Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NpgsqlMeterName = "Npgsql";
    /// <summary>Well-known N Service Bus Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NServiceBusMeterName = "NServiceBus.Core";
    /// <summary>Well-known NServiceBus incoming pipeline Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NServiceBusIncomingPipelineMeterName = "NServiceBus.Core.Pipeline.Incoming";
    /// <summary>Well-known Net Runtime Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NetRuntimeMeterName = "System.Runtime";
    /// <summary>Well-known Process Meter Name value used by qyl auto-instrumentation.</summary>
    public const string ProcessMeterName = "System.Runtime";

    /// <summary>Runs the Get Enabled Meter Names runtime helper used by source-generated qyl interceptors.</summary>
    public static string[] GetEnabledMeterNames()
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
        {
            names.Add(NpgsqlMeterName);
            databaseMeterEnabled = true;
        }

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.SqlClient))
            databaseMeterEnabled = true;

        if (databaseMeterEnabled)
            names.Add(DatabaseMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NServiceBus))
        {
            names.Add(NServiceBusMeterName);
            names.Add(NServiceBusIncomingPipelineMeterName);
        }

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NetRuntime))
            names.Add(NetRuntimeMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Process)
            && !names.Contains(ProcessMeterName, StringComparer.Ordinal))
            names.Add(ProcessMeterName);

        if (options.MetricsEnabled)
            AddDistinct(names, options.AdditionalMetricMeterNames);

        return names.ToArray();
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
