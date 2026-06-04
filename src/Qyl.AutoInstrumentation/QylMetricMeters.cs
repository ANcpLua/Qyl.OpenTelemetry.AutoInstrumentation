namespace Qyl.AutoInstrumentation;

/// <summary>
/// Meter names required by the metrics side of the auto-instrumentation contract.
/// These are registration targets for source-visible <c>MeterProviderBuilder.AddMeter(...)</c>
/// interception; they do not create instruments by themselves.
/// </summary>
public static class QylMetricMeters
{
    public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    public const string HttpClientMeterName = "System.Net.Http";
    public const string DatabaseMeterName = "Qyl.AutoInstrumentation.Database";
    public const string NpgsqlMeterName = "Npgsql";
    public const string NServiceBusMeterName = "NServiceBus.Core";
    public const string NetRuntimeMeterName = "OpenTelemetry.Instrumentation.Runtime";
    public const string ProcessMeterName = "OpenTelemetry.Instrumentation.Process";

    public static string[] GetEnabledMeterNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(8);

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.AspNetCore))
            names.Add(AspNetCoreComponentsMeterName);

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

        if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Metrics, QylAutoInstrumentationIds.Process))
            names.Add(ProcessMeterName);

        return names.ToArray();
    }
}
