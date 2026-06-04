using System.Collections.ObjectModel;
using System.Globalization;

namespace Qyl.AutoInstrumentation;

public sealed class QylAutoInstrumentationOptions
{
    private const string GlobalEnabledVariable = "OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED";
    private const string TracesEnabledVariable = "OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED";
    private const string MetricsEnabledVariable = "OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED";
    private const string LogsEnabledVariable = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED";
    private const string CaptureSensitiveValuesVariable = "QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES";

    public static readonly QylAutoInstrumentationOptions Current = Load();

    private readonly IReadOnlyDictionary<string, bool> _instrumentationEnabled;

    private QylAutoInstrumentationOptions(
        bool globalEnabled,
        bool tracesEnabled,
        bool metricsEnabled,
        bool logsEnabled,
        bool captureSensitiveValues,
        IReadOnlyDictionary<string, bool> instrumentationEnabled,
        bool entityFrameworkCoreSetDbStatementForText,
        bool graphQlSetDocument,
        bool oracleMdaSetDbStatementForText,
        bool sqlClientSetDbStatementForText,
        string[] aspNetCapturedRequestHeaders,
        string[] aspNetCapturedResponseHeaders,
        string[] aspNetCoreCapturedRequestHeaders,
        string[] aspNetCoreCapturedResponseHeaders,
        string[] grpcNetClientCapturedRequestMetadata,
        string[] grpcNetClientCapturedResponseMetadata,
        string[] httpClientCapturedRequestHeaders,
        string[] httpClientCapturedResponseHeaders,
        bool aspNetCoreUrlQueryRedactionDisabled,
        bool httpClientUrlQueryRedactionDisabled,
        bool aspNetUrlQueryRedactionDisabled,
        bool sqlClientNetFxIlRewriteRequested)
    {
        GlobalEnabled = globalEnabled;
        TracesEnabled = tracesEnabled;
        MetricsEnabled = metricsEnabled;
        LogsEnabled = logsEnabled;
        CaptureSensitiveValues = captureSensitiveValues;
        _instrumentationEnabled = instrumentationEnabled;
        EntityFrameworkCoreSetDbStatementForText = entityFrameworkCoreSetDbStatementForText;
        GraphQlSetDocument = graphQlSetDocument;
        OracleMdaSetDbStatementForText = oracleMdaSetDbStatementForText;
        SqlClientSetDbStatementForText = sqlClientSetDbStatementForText;
        AspNetCapturedRequestHeaders = aspNetCapturedRequestHeaders;
        AspNetCapturedResponseHeaders = aspNetCapturedResponseHeaders;
        AspNetCoreCapturedRequestHeaders = aspNetCoreCapturedRequestHeaders;
        AspNetCoreCapturedResponseHeaders = aspNetCoreCapturedResponseHeaders;
        GrpcNetClientCapturedRequestMetadata = grpcNetClientCapturedRequestMetadata;
        GrpcNetClientCapturedResponseMetadata = grpcNetClientCapturedResponseMetadata;
        HttpClientCapturedRequestHeaders = httpClientCapturedRequestHeaders;
        HttpClientCapturedResponseHeaders = httpClientCapturedResponseHeaders;
        AspNetCoreUrlQueryRedactionDisabled = aspNetCoreUrlQueryRedactionDisabled;
        HttpClientUrlQueryRedactionDisabled = httpClientUrlQueryRedactionDisabled;
        AspNetUrlQueryRedactionDisabled = aspNetUrlQueryRedactionDisabled;
        SqlClientNetFxIlRewriteRequested = sqlClientNetFxIlRewriteRequested;
    }

    public bool GlobalEnabled { get; }

    public bool TracesEnabled { get; }

    public bool MetricsEnabled { get; }

    public bool LogsEnabled { get; }

    public bool CaptureSensitiveValues { get; }

    public bool EntityFrameworkCoreSetDbStatementForText { get; }

    public bool GraphQlSetDocument { get; }

    public bool OracleMdaSetDbStatementForText { get; }

    public bool SqlClientSetDbStatementForText { get; }

    public string[] AspNetCapturedRequestHeaders { get; }

    public string[] AspNetCapturedResponseHeaders { get; }

    public string[] AspNetCoreCapturedRequestHeaders { get; }

    public string[] AspNetCoreCapturedResponseHeaders { get; }

    public string[] GrpcNetClientCapturedRequestMetadata { get; }

    public string[] GrpcNetClientCapturedResponseMetadata { get; }

    public string[] HttpClientCapturedRequestHeaders { get; }

    public string[] HttpClientCapturedResponseHeaders { get; }

    public bool AspNetCoreUrlQueryRedactionDisabled { get; }

    public bool HttpClientUrlQueryRedactionDisabled { get; }

    public bool AspNetUrlQueryRedactionDisabled { get; }

    public bool SqlClientNetFxIlRewriteRequested { get; }

    public bool SqlClientNetFxIlRewriteEnabled => false;

    public bool IsInstrumentationEnabled(QylAutoInstrumentationSignal signal, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);

        return _instrumentationEnabled.TryGetValue(BuildKey(signal, instrumentationId), out var enabled)
            ? enabled
            : IsSignalEnabled(signal);
    }

    public bool HasAnyActivityInstrumentationEnabled()
        => HasAnyInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, TraceInstrumentationIds) ||
           HasAnyInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, LogInstrumentationIds);

    private static QylAutoInstrumentationOptions Load()
    {
        var globalEnabled = ReadBoolean(GlobalEnabledVariable) ?? true;
        var tracesEnabled = ReadBoolean(TracesEnabledVariable) ?? globalEnabled;
        var metricsEnabled = ReadBoolean(MetricsEnabledVariable) ?? globalEnabled;
        var logsEnabled = ReadBoolean(LogsEnabledVariable) ?? globalEnabled;
        var instrumentationEnabled = new Dictionary<string, bool>(StringComparer.Ordinal);

        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Traces, tracesEnabled, TraceInstrumentationIds);
        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Metrics, metricsEnabled, MetricInstrumentationIds);
        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Logs, logsEnabled, LogInstrumentationIds);

        return new QylAutoInstrumentationOptions(
            globalEnabled,
            tracesEnabled,
            metricsEnabled,
            logsEnabled,
            ReadBoolean(CaptureSensitiveValuesVariable) ?? false,
            new ReadOnlyDictionary<string, bool>(instrumentationEnabled),
            ReadBoolean("OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT") ?? false,
            ReadBoolean("OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT") ?? false,
            ReadBoolean("OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT") ?? false,
            ReadBoolean("OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT") ?? false,
            ReadList("OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"),
            ReadList("OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"),
            ReadBoolean("OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION") ?? false,
            ReadBoolean("OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION") ?? false,
            ReadBoolean("OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION") ?? false,
            ReadBoolean("OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED") ?? false);
    }

    private static readonly string[] TraceInstrumentationIds =
    [
        QylAutoInstrumentationIds.AdoNet,
        QylAutoInstrumentationIds.AspNet,
        QylAutoInstrumentationIds.AspNetCore,
        QylAutoInstrumentationIds.Azure,
        QylAutoInstrumentationIds.Elasticsearch,
        QylAutoInstrumentationIds.ElasticTransport,
        QylAutoInstrumentationIds.EntityFrameworkCore,
        QylAutoInstrumentationIds.GraphQl,
        QylAutoInstrumentationIds.GrpcNetClient,
        QylAutoInstrumentationIds.HttpClient,
        QylAutoInstrumentationIds.Kafka,
        QylAutoInstrumentationIds.MassTransit,
        QylAutoInstrumentationIds.MongoDb,
        QylAutoInstrumentationIds.MySqlConnector,
        QylAutoInstrumentationIds.MySqlData,
        QylAutoInstrumentationIds.Npgsql,
        QylAutoInstrumentationIds.NServiceBus,
        QylAutoInstrumentationIds.OracleMda,
        QylAutoInstrumentationIds.RabbitMq,
        QylAutoInstrumentationIds.Quartz,
        QylAutoInstrumentationIds.SqlClient,
        QylAutoInstrumentationIds.Sqlite,
        QylAutoInstrumentationIds.StackExchangeRedis,
        QylAutoInstrumentationIds.WcfClient,
        QylAutoInstrumentationIds.WcfCore,
        QylAutoInstrumentationIds.WcfService,
    ];

    private static readonly string[] MetricInstrumentationIds =
    [
        QylAutoInstrumentationIds.AspNet,
        QylAutoInstrumentationIds.AspNetCore,
        QylAutoInstrumentationIds.HttpClient,
        QylAutoInstrumentationIds.NetRuntime,
        QylAutoInstrumentationIds.Npgsql,
        QylAutoInstrumentationIds.NServiceBus,
        QylAutoInstrumentationIds.Process,
        QylAutoInstrumentationIds.SqlClient,
    ];

    private static readonly string[] LogInstrumentationIds =
    [
        QylAutoInstrumentationIds.ILogger,
        QylAutoInstrumentationIds.Log4Net,
        QylAutoInstrumentationIds.NLog,
    ];

    private static void AddSignalInstrumentations(
        Dictionary<string, bool> target,
        QylAutoInstrumentationSignal signal,
        bool signalDefault,
        string[] instrumentationIds)
    {
        foreach (var instrumentationId in instrumentationIds)
        {
            var variable = BuildSignalSpecificVariable(signal, instrumentationId);
            target[BuildKey(signal, instrumentationId)] = ReadBoolean(variable) ?? signalDefault;
        }
    }

    private bool IsSignalEnabled(QylAutoInstrumentationSignal signal)
        => signal switch
        {
            QylAutoInstrumentationSignal.Traces => TracesEnabled,
            QylAutoInstrumentationSignal.Metrics => MetricsEnabled,
            QylAutoInstrumentationSignal.Logs => LogsEnabled,
            _ => false,
        };

    private bool HasAnyInstrumentationEnabled(QylAutoInstrumentationSignal signal, string[] instrumentationIds)
    {
        foreach (var instrumentationId in instrumentationIds)
        {
            if (IsInstrumentationEnabled(signal, instrumentationId))
                return true;
        }

        return false;
    }

    private static string BuildSignalSpecificVariable(QylAutoInstrumentationSignal signal, string instrumentationId)
        => signal switch
        {
            QylAutoInstrumentationSignal.Traces => "OTEL_DOTNET_AUTO_TRACES_" + instrumentationId + "_INSTRUMENTATION_ENABLED",
            QylAutoInstrumentationSignal.Metrics => "OTEL_DOTNET_AUTO_METRICS_" + instrumentationId + "_INSTRUMENTATION_ENABLED",
            QylAutoInstrumentationSignal.Logs => "OTEL_DOTNET_AUTO_LOGS_" + instrumentationId + "_INSTRUMENTATION_ENABLED",
            _ => throw new ArgumentOutOfRangeException(nameof(signal), signal, null),
        };

    private static string BuildKey(QylAutoInstrumentationSignal signal, string instrumentationId)
        => signal.ToString() + ":" + instrumentationId;

    private static bool? ReadBoolean(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return null;

        if (string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "1", StringComparison.Ordinal) ||
            string.Equals(value, "yes", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "on", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(value, "false", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "0", StringComparison.Ordinal) ||
            string.Equals(value, "no", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(value, "off", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return bool.TryParse(value, out var parsed) ? parsed : null;
    }

    private static string[] ReadList(string variable)
    {
        var value = Environment.GetEnvironmentVariable(variable);
        if (string.IsNullOrWhiteSpace(value))
            return [];

        return value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static item => item.Length > 0)
            .Select(static item => item.ToLower(CultureInfo.InvariantCulture))
            .Distinct(StringComparer.Ordinal)
            .ToArray();
    }
}
