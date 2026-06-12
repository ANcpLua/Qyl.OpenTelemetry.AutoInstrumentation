namespace Qyl.AutoInstrumentation;

/// <summary>
/// Meter names required by the metrics side of the auto-instrumentation contract.
/// These are registration targets for source-visible <c>MeterProviderBuilder.AddMeter(...)</c>
/// interception; they do not create instruments by themselves.
/// </summary>
public static class QylMetricMeters
{
    /// <summary>Well-known ASP.NET Core Components Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    /// <summary>Well-known ASP.NET Core Components Lifecycle Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";
    /// <summary>Well-known ASP.NET Core Components Server Circuits Meter Name value used by qyl auto-instrumentation.</summary>
    public const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";
    /// <summary>Well-known HTTP Client Meter Name value used by qyl auto-instrumentation.</summary>
    public const string HttpClientMeterName = "System.Net.Http";
    /// <summary>Well-known Database Meter Name value used by qyl auto-instrumentation.</summary>
    public const string DatabaseMeterName = "Qyl.AutoInstrumentation.Database";
    /// <summary>Well-known Npgsql Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NpgsqlMeterName = "Npgsql";
    /// <summary>Well-known N Service Bus Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NServiceBusMeterName = "NServiceBus.Core";
    /// <summary>Well-known Net Runtime Meter Name value used by qyl auto-instrumentation.</summary>
    public const string NetRuntimeMeterName = "System.Runtime";
    /// <summary>Well-known Process Meter Name value used by qyl auto-instrumentation.</summary>
    public const string ProcessMeterName = "System.Runtime";

    /// <summary>Runs the Get Enabled Meter Names runtime helper used by source-generated qyl interceptors.</summary>
    public static string[] GetEnabledMeterNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(10);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.AspNetCore))
        {
            names.Add(AspNetCoreComponentsMeterName);
            names.Add(AspNetCoreComponentsLifecycleMeterName);
            names.Add(AspNetCoreComponentsServerCircuitsMeterName);
        }

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.HttpClient))
            names.Add(HttpClientMeterName);

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
            names.Add(NServiceBusMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.NetRuntime))
            names.Add(NetRuntimeMeterName);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Process)
            && !names.Contains(ProcessMeterName, StringComparer.Ordinal))
            names.Add(ProcessMeterName);

        return names.ToArray();
    }
}
