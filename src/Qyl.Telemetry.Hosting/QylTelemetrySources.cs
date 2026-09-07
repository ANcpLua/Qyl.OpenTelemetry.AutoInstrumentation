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
    internal const string CoreWcf = QylTelemetryNames.VendorActivitySources.CoreWCFPrimitives;
    internal const string Azure = QylTelemetryNames.VendorActivitySources.Azure;
    internal const string AspNetCore = QylFrameworkActivitySources.AspNetCore;
    internal const string HttpClient = QylFrameworkActivitySources.HttpClient;
    internal const string ConnectorNet = QylTelemetryNames.VendorActivitySources.ConnectorNet;
    internal const string ElasticTransport = QylTelemetryNames.VendorActivitySources.ElasticTransport;
    internal const string GraphQl = QylTelemetryNames.VendorActivitySources.GraphQL;
    internal const string GrpcNetClient = QylTelemetryNames.VendorActivitySources.GrpcNetClient;
    internal const string MassTransit = QylTelemetryNames.VendorActivitySources.MassTransit;
    internal const string MongoDbDriver = QylTelemetryNames.VendorActivitySources.MongoDBDriver;
    internal const string MySqlConnector = QylTelemetryNames.VendorActivitySources.MySqlConnector;
    internal const string Npgsql = QylTelemetryNames.VendorActivitySources.Npgsql;
    internal const string OracleManagedDataAccessCore =
        QylTelemetryNames.VendorActivitySources.OracleManagedDataAccessCore;
    internal const string NServiceBusCore = QylTelemetryNames.VendorActivitySources.NServiceBusCore;
    internal const string Quartz = QylTelemetryNames.VendorActivitySources.Quartz;
    internal const string RabbitMqPublisher = QylTelemetryNames.VendorActivitySources.RabbitMQClientPublisher;
    internal const string RabbitMqSubscriber = QylTelemetryNames.VendorActivitySources.RabbitMQClientSubscriber;

    /// <summary>
    /// The libraries whose own <c>ActivitySource</c> qyl subscribes to instead of intercepting: the
    /// source name, the instrumentation id whose toggle gates it, and the domain stamped on its
    /// spans. One table drives both the <c>AddSource</c> calls and
    /// <see cref="QylNativeSpanProcessor"/>, so a library is added in one place.
    /// </summary>
    /// <remarks>
    /// CoreWCF carries no domain: its spans are the WCF <em>server</em> side, and the registry
    /// publishes no instrumentation-domain value for it — <c>rpc.wcf.client</c> belongs to the
    /// intercepted client. That missing value is a semantic-convention gap rather than a name to
    /// invent here, so the row exists for its <c>AddSource</c> call alone.
    /// </remarks>
    private static readonly QylNativeSourceRow[] NativeSourceRows =
    [
        new(Azure, QylAutoInstrumentationIds.Azure, QylAttributes.InstrumentationDomainValues.AzureSdk),
        new(CoreWcf, QylAutoInstrumentationIds.WcfCore, Domain: null),
        new(HttpClient, QylAutoInstrumentationIds.HttpClient, QylAttributes.InstrumentationDomainValues.HttpClient),
        new(ConnectorNet, QylAutoInstrumentationIds.MySqlData, QylAttributes.InstrumentationDomainValues.DbClient),
        new(
            ElasticTransport,
            QylAutoInstrumentationIds.ElasticTransport,
            QylAttributes.InstrumentationDomainValues.ElasticTransport),
        new(GraphQl, QylAutoInstrumentationIds.GraphQl, QylAttributes.InstrumentationDomainValues.GraphQl),
        new(
            GrpcNetClient,
            QylAutoInstrumentationIds.GrpcNetClient,
            QylAttributes.InstrumentationDomainValues.RpcGrpc),
        new(
            MassTransit,
            QylAutoInstrumentationIds.MassTransit,
            QylAttributes.InstrumentationDomainValues.MessagingMassTransit),
        new(MongoDbDriver, QylAutoInstrumentationIds.MongoDb, QylAttributes.InstrumentationDomainValues.DbMongoDb),
        new(
            MySqlConnector,
            QylAutoInstrumentationIds.MySqlConnector,
            QylAttributes.InstrumentationDomainValues.DbClient),
        new(Npgsql, QylAutoInstrumentationIds.Npgsql, QylAttributes.InstrumentationDomainValues.DbClient),
        new(
            NServiceBusCore,
            QylAutoInstrumentationIds.NServiceBus,
            QylAttributes.InstrumentationDomainValues.MessagingNServiceBus),
        new(
            OracleManagedDataAccessCore,
            QylAutoInstrumentationIds.OracleMda,
            QylAttributes.InstrumentationDomainValues.DbClient),
        new(Quartz, QylAutoInstrumentationIds.Quartz, QylAttributes.InstrumentationDomainValues.JobQuartz),
        new(
            RabbitMqPublisher,
            QylAutoInstrumentationIds.RabbitMq,
            QylAttributes.InstrumentationDomainValues.MessagingRabbitMq),
        new(
            RabbitMqSubscriber,
            QylAutoInstrumentationIds.RabbitMq,
            QylAttributes.InstrumentationDomainValues.MessagingRabbitMq),
    ];

    internal static string[] GetEnabledActivitySourceNames()
    {
        var options = QylAutoInstrumentationOptions.Current;
        var names = new List<string>(8 + NativeSourceRows.Length);

        if (options.HasAnyActivityInstrumentationEnabled())
            names.Add(QylActivitySource.Name);

        // Registering Microsoft.AspNetCore is what makes the hosting layer's own HttpRequestIn
        // activity — the one HTTP SERVER span of a request, which AddQylAspNetCoreInstrumentation's
        // middleware enriches — sampled and exported. The outbound side is System.Net.Http, and it
        // is an ordinary row of the native-source table below: the BCL writes the whole HTTP client
        // convention itself, so the processor only stamps the qyl domain onto it.
        AddIfEnabled(names, options, QylAutoInstrumentationIds.AspNetCore, AspNetCore);

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
