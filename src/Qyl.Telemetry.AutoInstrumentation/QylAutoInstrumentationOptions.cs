using System.Collections.ObjectModel;
using System.Globalization;
using Qyl.Telemetry.AutoInstrumentation.Internal;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;

namespace Qyl.Telemetry.AutoInstrumentation;

internal sealed class QylAutoInstrumentationOptions
{
    private const string GlobalEnabledVariable = "OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED";
    private const string TracesEnabledVariable = "OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED";
    private const string MetricsEnabledVariable = "OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED";
    private const string MetricsAdditionalSourcesVariable = "OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES";
    private const string LogsEnabledVariable = "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED";
    private const string EntityFrameworkCoreDbStatementVariable =
        "OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT";
    private const string GraphQlSetDocumentVariable = "OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT";
    private const string OracleMdaSetDbStatementVariable =
        "OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT";
    private const string SqlClientSetDbStatementVariable = "OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT";
    private const string AspNetCoreRequestHeadersVariable =
        "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS";
    private const string AspNetCoreResponseHeadersVariable =
        "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS";
    private const string GrpcClientRequestMetadataVariable =
        "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA";
    private const string GrpcClientResponseMetadataVariable =
        "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA";
    private const string HttpClientRequestHeadersVariable =
        "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS";
    private const string HttpClientResponseHeadersVariable =
        "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS";
    private const string AspNetCoreUrlQueryRedactionDisabledVariable =
        "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION";
    private const string HttpClientUrlQueryRedactionDisabledVariable =
        "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION";

    public static QylAutoInstrumentationOptions Current => CurrentHolder.Value;

    private readonly IReadOnlyDictionary<InstrumentationLookupKey, bool> _instrumentationEnabled;

    private QylAutoInstrumentationOptions(
        bool globalEnabled,
        bool tracesEnabled,
        bool metricsEnabled,
        bool logsEnabled,
        IReadOnlyDictionary<InstrumentationLookupKey, bool> instrumentationEnabled,
        bool entityFrameworkCoreSetDbStatementForText,
        bool graphQlSetDocument,
        bool oracleMdaSetDbStatementForText,
        bool sqlClientSetDbStatementForText,
        string[] aspNetCoreCapturedRequestHeaders,
        string[] aspNetCoreCapturedResponseHeaders,
        string[] grpcNetClientCapturedRequestMetadata,
        string[] grpcNetClientCapturedResponseMetadata,
        string[] httpClientCapturedRequestHeaders,
        string[] httpClientCapturedResponseHeaders,
        string[] additionalMetricMeterNames,
        bool aspNetCoreUrlQueryRedactionDisabled,
        bool httpClientUrlQueryRedactionDisabled)
    {
        GlobalEnabled = globalEnabled;
        TracesEnabled = tracesEnabled;
        MetricsEnabled = metricsEnabled;
        LogsEnabled = logsEnabled;
        _instrumentationEnabled = instrumentationEnabled;
        EntityFrameworkCoreSetDbStatementForText = entityFrameworkCoreSetDbStatementForText;
        GraphQlSetDocument = graphQlSetDocument;
        OracleMdaSetDbStatementForText = oracleMdaSetDbStatementForText;
        SqlClientSetDbStatementForText = sqlClientSetDbStatementForText;
        AspNetCoreCapturedRequestHeaders = aspNetCoreCapturedRequestHeaders;
        AspNetCoreCapturedResponseHeaders = aspNetCoreCapturedResponseHeaders;
        GrpcNetClientCapturedRequestMetadata = grpcNetClientCapturedRequestMetadata;
        GrpcNetClientCapturedResponseMetadata = grpcNetClientCapturedResponseMetadata;
        HttpClientCapturedRequestHeaders = httpClientCapturedRequestHeaders;
        HttpClientCapturedResponseHeaders = httpClientCapturedResponseHeaders;
        AspNetCoreCapturedRequestHeaderMap = QylCapturedNameMap.Create(HttpAttributes.RequestHeader + ".", aspNetCoreCapturedRequestHeaders);
        AspNetCoreCapturedResponseHeaderMap = QylCapturedNameMap.Create(HttpAttributes.ResponseHeader + ".", aspNetCoreCapturedResponseHeaders);
        GrpcNetClientCapturedRequestMetadataMap = QylCapturedNameMap.Create(RpcAttributes.RequestMetadata + ".", grpcNetClientCapturedRequestMetadata, normalizeLookupName: true);
        GrpcNetClientCapturedResponseMetadataMap = QylCapturedNameMap.Create(RpcAttributes.ResponseMetadata + ".", grpcNetClientCapturedResponseMetadata, normalizeLookupName: true);
        HttpClientCapturedRequestHeaderMap = QylCapturedNameMap.Create(HttpAttributes.RequestHeader + ".", httpClientCapturedRequestHeaders);
        HttpClientCapturedResponseHeaderMap = QylCapturedNameMap.Create(HttpAttributes.ResponseHeader + ".", httpClientCapturedResponseHeaders);
        AdditionalMetricMeterNames = additionalMetricMeterNames;
        AspNetCoreUrlQueryRedactionDisabled = aspNetCoreUrlQueryRedactionDisabled;
        HttpClientUrlQueryRedactionDisabled = httpClientUrlQueryRedactionDisabled;
    }

    public bool GlobalEnabled { get; }

    public bool TracesEnabled { get; }

    public bool MetricsEnabled { get; }

    public bool LogsEnabled { get; }

    public bool EntityFrameworkCoreSetDbStatementForText { get; }

    public bool GraphQlSetDocument { get; }

    public bool OracleMdaSetDbStatementForText { get; }

    public bool SqlClientSetDbStatementForText { get; }

    public string[] AspNetCoreCapturedRequestHeaders { get; }

    public string[] AspNetCoreCapturedResponseHeaders { get; }

    public string[] GrpcNetClientCapturedRequestMetadata { get; }

    public string[] GrpcNetClientCapturedResponseMetadata { get; }

    public string[] HttpClientCapturedRequestHeaders { get; }

    public string[] HttpClientCapturedResponseHeaders { get; }

    internal QylCapturedNameMap AspNetCoreCapturedRequestHeaderMap { get; }

    internal QylCapturedNameMap AspNetCoreCapturedResponseHeaderMap { get; }

    internal QylCapturedNameMap GrpcNetClientCapturedRequestMetadataMap { get; }

    internal QylCapturedNameMap GrpcNetClientCapturedResponseMetadataMap { get; }

    internal QylCapturedNameMap HttpClientCapturedRequestHeaderMap { get; }

    internal QylCapturedNameMap HttpClientCapturedResponseHeaderMap { get; }

    internal string[] AdditionalMetricMeterNames { get; }

    public bool AspNetCoreUrlQueryRedactionDisabled { get; }

    public bool HttpClientUrlQueryRedactionDisabled { get; }

    public bool IsInstrumentationEnabled(QylAutoInstrumentationSignal signal, string instrumentationId)
    {
        ArgumentNullException.ThrowIfNull(instrumentationId);

        return _instrumentationEnabled.TryGetValue(new InstrumentationLookupKey(signal, instrumentationId), out var enabled)
            ? enabled
            : IsSignalEnabled(signal);
    }

    public bool HasAnyActivityInstrumentationEnabled()
        => HasAnyInstrumentationEnabled(QylAutoInstrumentationSignal.Traces, QylDeclaredInstrumentations.Traces) ||
           HasAnyInstrumentationEnabled(QylAutoInstrumentationSignal.Logs, QylDeclaredInstrumentations.Logs);

    private static class CurrentHolder
    {
        internal static readonly QylAutoInstrumentationOptions Value = Load();
    }

    private static QylAutoInstrumentationOptions Load()
    {
        var globalEnabled = EnvironmentOptions.ReadBoolean(GlobalEnabledVariable) ?? true;
        var tracesEnabled = EnvironmentOptions.ReadBoolean(TracesEnabledVariable) ?? globalEnabled;
        var metricsEnabled = EnvironmentOptions.ReadBoolean(MetricsEnabledVariable) ?? globalEnabled;
        var logsEnabled = EnvironmentOptions.ReadBoolean(LogsEnabledVariable) ?? globalEnabled;
        var instrumentationEnabled = new Dictionary<InstrumentationLookupKey, bool>();

        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Traces, tracesEnabled, QylDeclaredInstrumentations.Traces);
        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Metrics, metricsEnabled, QylDeclaredInstrumentations.Metrics);
        AddSignalInstrumentations(instrumentationEnabled, QylAutoInstrumentationSignal.Logs, logsEnabled, QylDeclaredInstrumentations.Logs);

        return new QylAutoInstrumentationOptions(
            globalEnabled,
            tracesEnabled,
            metricsEnabled,
            logsEnabled,
            new ReadOnlyDictionary<InstrumentationLookupKey, bool>(instrumentationEnabled),
            EnvironmentOptions.ReadBoolean(EntityFrameworkCoreDbStatementVariable) ?? false,
            EnvironmentOptions.ReadBoolean(GraphQlSetDocumentVariable) ?? false,
            EnvironmentOptions.ReadBoolean(OracleMdaSetDbStatementVariable) ?? false,
            EnvironmentOptions.ReadBoolean(SqlClientSetDbStatementVariable) ?? false,
            EnvironmentOptions.ReadList(AspNetCoreRequestHeadersVariable),
            EnvironmentOptions.ReadList(AspNetCoreResponseHeadersVariable),
            EnvironmentOptions.ReadList(GrpcClientRequestMetadataVariable),
            EnvironmentOptions.ReadList(GrpcClientResponseMetadataVariable),
            EnvironmentOptions.ReadList(HttpClientRequestHeadersVariable),
            EnvironmentOptions.ReadList(HttpClientResponseHeadersVariable),
            EnvironmentOptions.ReadCaseSensitiveList(MetricsAdditionalSourcesVariable),
            EnvironmentOptions.ReadBoolean(AspNetCoreUrlQueryRedactionDisabledVariable) ?? false,
            EnvironmentOptions.ReadBoolean(HttpClientUrlQueryRedactionDisabledVariable) ?? false);
    }

    private static void AddSignalInstrumentations(
        Dictionary<InstrumentationLookupKey, bool> target,
        QylAutoInstrumentationSignal signal,
        bool signalDefault,
        string[] instrumentationIds)
    {
        ArgumentNullException.ThrowIfNull(instrumentationIds);

        foreach (var instrumentationId in instrumentationIds)
        {
            if (string.IsNullOrWhiteSpace(instrumentationId))
                continue;

            var variable = BuildSignalSpecificVariable(signal, instrumentationId);
            target[new InstrumentationLookupKey(signal, instrumentationId)] =
                EnvironmentOptions.ReadBoolean(variable) ?? signalDefault;
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

    private static class EnvironmentOptions
    {
        internal static bool? ReadBoolean(string variable)
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

        internal static string[] ReadList(string variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                return [];

            return
            [
                .. value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static item => item.Length > 0)
                    .Select(static item => item.ToLower(CultureInfo.InvariantCulture))
                    .Distinct(StringComparer.Ordinal)
            ];
        }

        internal static string[] ReadCaseSensitiveList(string variable)
        {
            var value = Environment.GetEnvironmentVariable(variable);
            if (string.IsNullOrWhiteSpace(value))
                return [];

            return
            [
                .. value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static item => item.Length > 0)
                    .Distinct(StringComparer.Ordinal)
            ];
        }
    }

    private readonly struct InstrumentationLookupKey : IEquatable<InstrumentationLookupKey>
    {
        private readonly QylAutoInstrumentationSignal signal;
        private readonly string instrumentationId;

        internal InstrumentationLookupKey(QylAutoInstrumentationSignal signal, string instrumentationId)
        {
            this.signal = signal;
            this.instrumentationId = instrumentationId;
        }

        public bool Equals(InstrumentationLookupKey other)
            => signal == other.signal &&
               string.Equals(instrumentationId, other.instrumentationId, StringComparison.Ordinal);

        public override bool Equals(object? obj)
            => obj is InstrumentationLookupKey other && Equals(other);

        public override int GetHashCode()
            => HashCode.Combine(
                signal,
                instrumentationId is null ? 0 : StringComparer.Ordinal.GetHashCode(instrumentationId));
    }
}
