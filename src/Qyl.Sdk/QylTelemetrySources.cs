using Qyl.OpenTelemetry.AutoInstrumentation;

namespace Qyl;

internal static class QylTelemetrySources
{
    internal const string MicrosoftExtensionsAi = "Experimental.Microsoft.Extensions.AI";
    internal const string MicrosoftAgentsAi = "Experimental.Microsoft.Agents.AI";
    internal const string MicrosoftAgentsAiWorkflows = "Microsoft.Agents.AI.Workflows";
    internal const string ModelContextProtocol = "Experimental.ModelContextProtocol";
    internal const string CoreWcf = "CoreWCF.Primitives";
    internal const string Azure = "Azure.*";

    internal static string[] GetEnabledActivitySourceNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(6);

        if (options.HasAnyActivityInstrumentationEnabled())
            names.Add(QylActivitySource.Name);

        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftExtensionsAi, MicrosoftExtensionsAi);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftAgentsAi, MicrosoftAgentsAi);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftAgentsAiWorkflows, MicrosoftAgentsAiWorkflows);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.ModelContextProtocol, ModelContextProtocol);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.WcfCore, CoreWcf);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.Azure, Azure);

        return names.ToArray();
    }

    internal static bool IsAzureTracingEnabled()
        => QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(
            QylAutoInstrumentationSignal.Traces,
            QylAutoInstrumentationIds.Azure);

    internal static string[] GetEnabledMeterNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(2);

        AddIfEnabled(
            names,
            options,
            QylAutoInstrumentationIds.MicrosoftExtensionsAi,
            MicrosoftExtensionsAi,
            QylAutoInstrumentationSignal.Metrics);
        AddIfEnabled(
            names,
            options,
            QylAutoInstrumentationIds.MicrosoftAgentsAi,
            MicrosoftAgentsAi,
            QylAutoInstrumentationSignal.Metrics);

        return names.ToArray();
    }

    private static void AddIfEnabled(
        List<string> names,
        QylAutoInstrumentationOptions options,
        string instrumentationId,
        string telemetryName,
        QylAutoInstrumentationSignal signal = QylAutoInstrumentationSignal.Traces)
    {
        if (options.IsInstrumentationEnabled(signal, instrumentationId))
            names.Add(telemetryName);
    }
}
