using Qyl.Telemetry.AutoInstrumentation;
using Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

namespace Qyl;

internal static class QylTelemetrySources
{
    internal const string MicrosoftExtensionsAi = "Experimental.Microsoft.Extensions.AI";
    internal const string MicrosoftAgentsAi = "Experimental.Microsoft.Agents.AI";
    internal const string MicrosoftAgentsAiWorkflows = "Microsoft.Agents.AI.Workflows";
    internal const string ModelContextProtocol = "Experimental.ModelContextProtocol";
    internal const string CoreWcf = "CoreWCF.Primitives";
    internal const string Azure = "Azure.*";
    internal const string AspNetCore = "Microsoft.AspNetCore";
    internal const string HttpClient = "System.Net.Http";
    internal const string ElasticTransport = QylTelemetryNames.VendorActivitySources.ElasticTransport;
    internal const string MassTransit = QylTelemetryNames.VendorActivitySources.MassTransit;
    internal const string RabbitMqPublisher = QylTelemetryNames.VendorActivitySources.RabbitMQClientPublisher;
    internal const string RabbitMqSubscriber = QylTelemetryNames.VendorActivitySources.RabbitMQClientSubscriber;

    /// <summary>
    /// The libraries whose own <c>ActivitySource</c> qyl subscribes to instead of intercepting: the
    /// source name, the instrumentation id whose toggle gates it, the domain stamped on its spans,
    /// and the normalisation that source needs. One table drives both the <c>AddSource</c> calls and
    /// <see cref="QylNativeSpanProcessor"/>, so a library is added in one place.
    /// </summary>
    /// <remarks>
    /// CoreWCF carries no domain: its spans are the WCF <em>server</em> side, and the registry
    /// publishes no instrumentation-domain value for it — <c>rpc.wcf.client</c> belongs to the
    /// intercepted client. The row still normalises the span exactly as before, and the missing
    /// value is a semantic-convention gap rather than a name to invent here.
    /// </remarks>
    private static readonly QylNativeSourceRow[] NativeSourceRows =
    [
        new(
            Azure,
            QylAutoInstrumentationIds.Azure,
            QylAttributes.InstrumentationDomainValues.AzureSdk,
            QylNativeSpanProcessor.NormalizeAzure),
        new(
            CoreWcf,
            QylAutoInstrumentationIds.WcfCore,
            Domain: null,
            QylNativeSpanProcessor.NormalizeCoreWcf),
        new(
            ElasticTransport,
            QylAutoInstrumentationIds.ElasticTransport,
            QylAttributes.InstrumentationDomainValues.ElasticTransport,
            QylNativeSpanProcessor.NormalizeElastic),
        new(
            MassTransit,
            QylAutoInstrumentationIds.MassTransit,
            QylAttributes.InstrumentationDomainValues.MessagingMassTransit,
            Normalize: null),
        new(
            RabbitMqPublisher,
            QylAutoInstrumentationIds.RabbitMq,
            QylAttributes.InstrumentationDomainValues.MessagingRabbitMq,
            Normalize: null),
        new(
            RabbitMqSubscriber,
            QylAutoInstrumentationIds.RabbitMq,
            QylAttributes.InstrumentationDomainValues.MessagingRabbitMq,
            Normalize: null),
    ];

    internal static string[] GetEnabledActivitySourceNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(8 + NativeSourceRows.Length);

        if (options.HasAnyActivityInstrumentationEnabled())
            names.Add(QylActivitySource.Name);

        // Framework-native sources: registering them makes ASP.NET Core hosting and HttpClient
        // create their activities through the sampler (proper root sampling decisions, honored
        // upstream traceparent) instead of the legacy unsampled DiagnosticListener fallback.
        AddIfEnabled(names, options, QylAutoInstrumentationIds.AspNetCore, AspNetCore);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.HttpClient, HttpClient);

        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftExtensionsAi, MicrosoftExtensionsAi);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftAgentsAi, MicrosoftAgentsAi);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.MicrosoftAgentsAiWorkflows, MicrosoftAgentsAiWorkflows);
        AddIfEnabled(names, options, QylAutoInstrumentationIds.ModelContextProtocol, ModelContextProtocol);

        foreach (var row in GetEnabledNativeSourceRows())
            names.Add(row.SourceName);

        return [.. names];
    }

    internal static QylNativeSourceRow[] GetEnabledNativeSourceRows()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var rows = new List<QylNativeSourceRow>(NativeSourceRows.Length);

        foreach (var row in NativeSourceRows)
        {
            if (options.IsInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, row.InstrumentationId))
                rows.Add(row);
        }

        return [.. rows];
    }

    internal static bool IsLogRecordCaptureEnabled()
        => QylAutoInstrumentationOptions.Current.LogsEnabled;

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
        return [.. names];
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
