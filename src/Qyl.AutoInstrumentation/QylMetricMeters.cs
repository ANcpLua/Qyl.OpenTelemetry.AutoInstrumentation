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
}
