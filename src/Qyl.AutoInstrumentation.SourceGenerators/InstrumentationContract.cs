using System.Collections.Immutable;
using System.Text;

namespace Qyl.AutoInstrumentation.SourceGenerators;

internal enum InstrumentationContractKind
{
    SignalSpecificInstrumentationPromise,
    GlobalEnvironmentControl,
    InstrumentationOption,
}

internal enum InstrumentationSignal
{
    None,
    Traces,
    Metrics,
    Logs,
}

internal readonly record struct InstrumentationContractItem(
    int Index,
    string ContractItemId,
    InstrumentationContractKind Kind,
    string Key,
    InstrumentationSignal Signal,
    string InstrumentationId,
    string EnvironmentVariable,
    string SupportedVersions,
    ImmutableArray<string> Libraries,
    ImmutableArray<string> InstrumentationTypes,
    string Promise,
    ImmutableArray<string> AttributeNames);

internal static class InstrumentationContract
{
    public const int SignalSpecificInstrumentationPromiseCount = 37;
    public const int GlobalEnvironmentControlCount = 7;
    public const int InstrumentationOptionCount = 16;
    public const int TotalCount =
        SignalSpecificInstrumentationPromiseCount +
        GlobalEnvironmentControlCount +
        InstrumentationOptionCount;

    public const int TracesSignalSpecificPromiseCount = 26;
    public const int MetricsSignalSpecificPromiseCount = 8;
    public const int LogsSignalSpecificPromiseCount = 3;
    public const int UniqueInstrumentationIdCount = 31;
    public const int UnsupportedNativeAotSignalPromiseCount = 3;
    public const int SourceGeneratedSignalPromiseCount =
        SignalSpecificInstrumentationPromiseCount -
        UnsupportedNativeAotSignalPromiseCount;

    public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";
    public const string AspNetCoreComponentsNavigationMetricName = "aspnetcore.components.navigation";

    public static readonly ImmutableArray<string> UnsupportedNativeAotSignalKeys =
    [
        "signals.traces.ASPNET",
        "signals.traces.WCFSERVICE",
        "signals.metrics.ASPNET",
    ];

    public static readonly ImmutableArray<InstrumentationContractItem> Items =
    [
        Signal(1, "signals.traces.ADONET", InstrumentationSignal.Traces, "ADONET", "OTEL_DOTNET_AUTO_TRACES_ADONET_INSTRUMENTATION_ENABLED", "ADO.NET", "System.Data.Common >=4.0.0 && <11.0.0; System.Data for .NET Framework >=2.0.0 && <5.0.0; netstandard >=2.0.0 && <3.0.0", "bytecode", "DbCommand instrumentation promise. NativeAOT replacement must use source-visible DbCommand call interception; runtime IL rewriting is forbidden."),
        Signal(2, "signals.traces.ASPNET", InstrumentationSignal.Traces, "ASPNET", "OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_ENABLED", "ASP.NET (.NET Framework) MVC / WebApi", "*", "source|bytecode", "ASP.NET Framework tracing promise. Not supported for .NET NativeAOT; retained only for contract parity."),
        Signal(3, "signals.traces.ASPNETCORE", InstrumentationSignal.Traces, "ASPNETCORE", "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_ENABLED", "ASP.NET Core", "*", "source", "ASP.NET Core tracing promise for source-visible pipeline and endpoint surfaces on .NET 10."),
        Signal(4, "signals.traces.AZURE", InstrumentationSignal.Traces, "AZURE", "OTEL_DOTNET_AUTO_TRACES_AZURE_INSTRUMENTATION_ENABLED", "Azure SDK", "Azure.* packages released after 2021-10-01", "source", "Azure SDK tracing promise."),
        Signal(5, "signals.traces.ELASTICSEARCH", InstrumentationSignal.Traces, "ELASTICSEARCH", "OTEL_DOTNET_AUTO_TRACES_ELASTICSEARCH_INSTRUMENTATION_ENABLED", "Elastic.Clients.Elasticsearch", ">=8.0.0 && <8.10.0", "source", "Elastic.Clients.Elasticsearch tracing promise for versions before Elastic.Transport takes over."),
        Signal(6, "signals.traces.ELASTICTRANSPORT", InstrumentationSignal.Traces, "ELASTICTRANSPORT", "OTEL_DOTNET_AUTO_TRACES_ELASTICTRANSPORT_INSTRUMENTATION_ENABLED", "Elastic.Transport", ">=0.4.16", "source", "Elastic.Transport tracing promise."),
        Signal(7, "signals.traces.ENTITYFRAMEWORKCORE", InstrumentationSignal.Traces, "ENTITYFRAMEWORKCORE", "OTEL_DOTNET_AUTO_TRACES_ENTITYFRAMEWORKCORE_INSTRUMENTATION_ENABLED", "Microsoft.EntityFrameworkCore", ">=6.0.12", "source", "Entity Framework Core tracing promise for source-visible EF Core query and DbContext calls."),
        Signal(8, "signals.traces.GRAPHQL", InstrumentationSignal.Traces, "GRAPHQL", "OTEL_DOTNET_AUTO_TRACES_GRAPHQL_INSTRUMENTATION_ENABLED", "GraphQL", ">=7.5.0", "source", "GraphQL tracing promise."),
        Signal(9, "signals.traces.GRPCNETCLIENT", InstrumentationSignal.Traces, "GRPCNETCLIENT", "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_ENABLED", "Grpc.Net.Client", ">=2.52.0 && <3.0.0", "source", "Grpc.Net.Client tracing promise."),
        Signal(10, "signals.traces.HTTPCLIENT", InstrumentationSignal.Traces, "HTTPCLIENT", "OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED", "System.Net.Http.HttpClient|System.Net.HttpWebRequest", "*", "source", "HTTP client tracing promise for source-visible HttpClient calls."),
        Signal(11, "signals.traces.KAFKA", InstrumentationSignal.Traces, "KAFKA", "OTEL_DOTNET_AUTO_TRACES_KAFKA_INSTRUMENTATION_ENABLED", "Confluent.Kafka", ">=1.4.0 && <3.0.0", "bytecode", "Kafka tracing promise. NativeAOT replacement must use source-visible producer and consumer call interception; runtime IL rewriting is forbidden."),
        Signal(12, "signals.traces.MASSTRANSIT", InstrumentationSignal.Traces, "MASSTRANSIT", "OTEL_DOTNET_AUTO_TRACES_MASSTRANSIT_INSTRUMENTATION_ENABLED", "MassTransit", ">=8.0.0", "source", "MassTransit tracing promise."),
        Signal(13, "signals.traces.MONGODB", InstrumentationSignal.Traces, "MONGODB", "OTEL_DOTNET_AUTO_TRACES_MONGODB_INSTRUMENTATION_ENABLED", "MongoDB.Driver.Core|MongoDB.Driver", ">=2.7.0", "source|bytecode", "MongoDB tracing promise. NativeAOT mode must prefer source instrumentation and must not use bytecode rewriting."),
        Signal(14, "signals.traces.MYSQLCONNECTOR", InstrumentationSignal.Traces, "MYSQLCONNECTOR", "OTEL_DOTNET_AUTO_TRACES_MYSQLCONNECTOR_INSTRUMENTATION_ENABLED", "MySqlConnector", ">=2.0.0", "source", "MySqlConnector tracing promise."),
        Signal(15, "signals.traces.MYSQLDATA", InstrumentationSignal.Traces, "MYSQLDATA", "OTEL_DOTNET_AUTO_TRACES_MYSQLDATA_INSTRUMENTATION_ENABLED", "MySql.Data", ">=8.1.0", "source", "MySql.Data tracing promise."),
        Signal(16, "signals.traces.NPGSQL", InstrumentationSignal.Traces, "NPGSQL", "OTEL_DOTNET_AUTO_TRACES_NPGSQL_INSTRUMENTATION_ENABLED", "Npgsql", ">=6.0.0", "source", "Npgsql tracing promise."),
        Signal(17, "signals.traces.NSERVICEBUS", InstrumentationSignal.Traces, "NSERVICEBUS", "OTEL_DOTNET_AUTO_TRACES_NSERVICEBUS_INSTRUMENTATION_ENABLED", "NServiceBus", ">=8.0.0 && <10.0.0", "source|bytecode", "NServiceBus tracing promise. NativeAOT replacement must not use bytecode rewriting."),
        Signal(18, "signals.traces.ORACLEMDA", InstrumentationSignal.Traces, "ORACLEMDA", "OTEL_DOTNET_AUTO_TRACES_ORACLEMDA_INSTRUMENTATION_ENABLED", "Oracle.ManagedDataAccess.Core|Oracle.ManagedDataAccess", ">=23.4.0", "source", "Oracle managed data access tracing promise."),
        Signal(19, "signals.traces.RABBITMQ", InstrumentationSignal.Traces, "RABBITMQ", "OTEL_DOTNET_AUTO_TRACES_RABBITMQ_INSTRUMENTATION_ENABLED", "RabbitMQ.Client", ">=5.0.0", "source|bytecode", "RabbitMQ tracing promise. NativeAOT mode must prefer source instrumentation and must not use bytecode rewriting."),
        Signal(20, "signals.traces.QUARTZ", InstrumentationSignal.Traces, "QUARTZ", "OTEL_DOTNET_AUTO_TRACES_QUARTZ_INSTRUMENTATION_ENABLED", "Quartz", ">=3.4.0", "source", "Quartz tracing promise."),
        Signal(21, "signals.traces.SQLCLIENT", InstrumentationSignal.Traces, "SQLCLIENT", "OTEL_DOTNET_AUTO_TRACES_SQLCLIENT_INSTRUMENTATION_ENABLED", "Microsoft.Data.SqlClient|System.Data.SqlClient|System.Data", "*", "source", "SQL Client tracing promise for source-visible command calls."),
        Signal(22, "signals.traces.SQLITE", InstrumentationSignal.Traces, "SQLITE", "OTEL_DOTNET_AUTO_TRACES_SQLITE_INSTRUMENTATION_ENABLED", "Microsoft.Data.Sqlite", ">=8.0.0 && <11.0.0", "bytecode", "SQLite tracing promise. NativeAOT replacement must use source-visible command interception; runtime IL rewriting is forbidden."),
        Signal(23, "signals.traces.STACKEXCHANGEREDIS", InstrumentationSignal.Traces, "STACKEXCHANGEREDIS", "OTEL_DOTNET_AUTO_TRACES_STACKEXCHANGEREDIS_INSTRUMENTATION_ENABLED", "StackExchange.Redis", ">=2.6.122 && <3.0.0", "source|bytecode", "StackExchange.Redis tracing promise. NativeAOT replacement must not use bytecode rewriting."),
        Signal(24, "signals.traces.WCFCLIENT", InstrumentationSignal.Traces, "WCFCLIENT", "OTEL_DOTNET_AUTO_TRACES_WCFCLIENT_INSTRUMENTATION_ENABLED", "WCF", "*", "source|bytecode", "WCF client tracing promise. Not supported for .NET NativeAOT; retained only for contract parity."),
        Signal(25, "signals.traces.WCFCORE", InstrumentationSignal.Traces, "WCFCORE", "OTEL_DOTNET_AUTO_TRACES_WCFCORE_INSTRUMENTATION_ENABLED", "CoreWCF.Primitives", ">=1.8.0", "source", "CoreWCF tracing promise."),
        Signal(26, "signals.traces.WCFSERVICE", InstrumentationSignal.Traces, "WCFSERVICE", "OTEL_DOTNET_AUTO_TRACES_WCFSERVICE_INSTRUMENTATION_ENABLED", "WCF", "*", "source|bytecode", "WCF service tracing promise. Not supported for .NET NativeAOT; retained only for contract parity."),
        Signal(27, "signals.metrics.ASPNET", InstrumentationSignal.Metrics, "ASPNET", "OTEL_DOTNET_AUTO_METRICS_ASPNET_INSTRUMENTATION_ENABLED", "ASP.NET Framework", "*", "source|bytecode", "ASP.NET Framework metrics promise. Not supported for .NET NativeAOT; retained only for contract parity."),
        Signal(28, "signals.metrics.ASPNETCORE", InstrumentationSignal.Metrics, "ASPNETCORE", "OTEL_DOTNET_AUTO_METRICS_ASPNETCORE_INSTRUMENTATION_ENABLED", "ASP.NET Core", "*", "source", "ASP.NET Core .NET 10 metrics promise. Use built-in .NET 10 meter Microsoft.AspNetCore.Components and metric aspnetcore.components.navigation; do not regress to Microsoft.AspNetCore.Hosting/http.server.request.duration as the primary proof. Do not re-emit NavigationManager.NavigateTo as aspnetcore.components.navigation because the required component type and route are owned by ASP.NET Core internals, not the call-site."),
        Signal(29, "signals.metrics.HTTPCLIENT", InstrumentationSignal.Metrics, "HTTPCLIENT", "OTEL_DOTNET_AUTO_METRICS_HTTPCLIENT_INSTRUMENTATION_ENABLED", "System.Net.Http.HttpClient|System.Net.HttpWebRequest", "*", "source", "HTTP client metrics promise."),
        Signal(30, "signals.metrics.NETRUNTIME", InstrumentationSignal.Metrics, "NETRUNTIME", "OTEL_DOTNET_AUTO_METRICS_NETRUNTIME_INSTRUMENTATION_ENABLED", "OpenTelemetry.Instrumentation.Runtime", "*", "source", "Runtime metrics promise."),
        Signal(31, "signals.metrics.NPGSQL", InstrumentationSignal.Metrics, "NPGSQL", "OTEL_DOTNET_AUTO_METRICS_NPGSQL_INSTRUMENTATION_ENABLED", "Npgsql", ">=6.0.0", "source", "Npgsql metrics promise."),
        Signal(32, "signals.metrics.NSERVICEBUS", InstrumentationSignal.Metrics, "NSERVICEBUS", "OTEL_DOTNET_AUTO_METRICS_NSERVICEBUS_INSTRUMENTATION_ENABLED", "NServiceBus", ">=8.0.0 && <10.0.0", "source|bytecode", "NServiceBus metrics promise. NativeAOT replacement must not use bytecode rewriting."),
        Signal(33, "signals.metrics.PROCESS", InstrumentationSignal.Metrics, "PROCESS", "OTEL_DOTNET_AUTO_METRICS_PROCESS_INSTRUMENTATION_ENABLED", "OpenTelemetry.Instrumentation.Process", "*", "source", "Process metrics promise."),
        Signal(34, "signals.metrics.SQLCLIENT", InstrumentationSignal.Metrics, "SQLCLIENT", "OTEL_DOTNET_AUTO_METRICS_SQLCLIENT_INSTRUMENTATION_ENABLED", "Microsoft.Data.SqlClient|System.Data.SqlClient|System.Data", "*", "source", "SQL Client metrics promise."),
        Signal(35, "signals.logs.ILOGGER", InstrumentationSignal.Logs, "ILOGGER", "OTEL_DOTNET_AUTO_LOGS_ILOGGER_INSTRUMENTATION_ENABLED", "Microsoft.Extensions.Logging", ">=8.0.0", "bytecode|source", "Microsoft.Extensions.Logging logs promise. NativeAOT mode must use source-visible logging surfaces and no hosting startup assembly."),
        Signal(36, "signals.logs.LOG4NET", InstrumentationSignal.Logs, "LOG4NET", "OTEL_DOTNET_AUTO_LOGS_LOG4NET_INSTRUMENTATION_ENABLED", "log4net", ">=2.0.13 && <4.0.0", "bytecode", "log4net logs promise. NativeAOT replacement must not use bytecode rewriting."),
        Signal(37, "signals.logs.NLOG", InstrumentationSignal.Logs, "NLOG", "OTEL_DOTNET_AUTO_LOGS_NLOG_INSTRUMENTATION_ENABLED", "NLog", ">=5.0.0 && <7.0.0", "bytecode", "NLog logs promise. NativeAOT replacement must not use bytecode rewriting."),

        Control(38, "global_environment_controls.OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED", InstrumentationSignal.None, "Enables all instrumentations. Default true."),
        Control(39, "global_environment_controls.OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED", InstrumentationSignal.Traces, "Enables all trace instrumentations and overrides the global default."),
        Control(40, "global_environment_controls.OTEL_DOTNET_AUTO_TRACES_{0}_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_TRACES_{0}_INSTRUMENTATION_ENABLED", InstrumentationSignal.Traces, "Enables a specific trace instrumentation and overrides the trace default."),
        Control(41, "global_environment_controls.OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED", InstrumentationSignal.Metrics, "Enables all metric instrumentations and overrides the global default."),
        Control(42, "global_environment_controls.OTEL_DOTNET_AUTO_METRICS_{0}_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_METRICS_{0}_INSTRUMENTATION_ENABLED", InstrumentationSignal.Metrics, "Enables a specific metric instrumentation and overrides the metric default."),
        Control(43, "global_environment_controls.OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED", InstrumentationSignal.Logs, "Enables all log instrumentations and overrides the global default."),
        Control(44, "global_environment_controls.OTEL_DOTNET_AUTO_LOGS_{0}_INSTRUMENTATION_ENABLED", "OTEL_DOTNET_AUTO_LOGS_{0}_INSTRUMENTATION_ENABLED", InstrumentationSignal.Logs, "Enables a specific log instrumentation and overrides the log default."),

        Option(45, "instrumentation_options.OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT", "OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT", InstrumentationSignal.Traces, "ENTITYFRAMEWORKCORE", "Whether EF Core can emit db.query.text for text commands. The upstream option name still says DBSTATEMENT.", "db.query.text"),
        Option(46, "instrumentation_options.OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT", "OTEL_DOTNET_AUTO_GRAPHQL_SET_DOCUMENT", InstrumentationSignal.Traces, "GRAPHQL", "Whether GraphQL can emit graphql.document.", "graphql.document"),
        Option(47, "instrumentation_options.OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT", "OTEL_DOTNET_AUTO_ORACLEMDA_SET_DBSTATEMENT_FOR_TEXT", InstrumentationSignal.Traces, "ORACLEMDA", "Whether Oracle MDA can emit db.query.text for text commands. The upstream option name still says DBSTATEMENT.", "db.query.text"),
        Option(48, "instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT", "OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT", InstrumentationSignal.Traces, "SQLCLIENT", "Whether SQL Client can emit db.query.text for text commands. The upstream option name still says DBSTATEMENT.", "db.query.text"),
        Option(49, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", "OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", InstrumentationSignal.Traces, "ASPNET", "Comma-separated ASP.NET request headers to capture.", "http.request.header"),
        Option(50, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", "OTEL_DOTNET_AUTO_TRACES_ASPNET_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", InstrumentationSignal.Traces, "ASPNET", "Comma-separated ASP.NET response headers to capture.", "http.response.header"),
        Option(51, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", InstrumentationSignal.Traces, "ASPNETCORE", "Comma-separated ASP.NET Core request headers to capture.", "http.request.header"),
        Option(52, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", "OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", InstrumentationSignal.Traces, "ASPNETCORE", "Comma-separated ASP.NET Core response headers to capture.", "http.response.header"),
        Option(53, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA", "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_REQUEST_METADATA", InstrumentationSignal.Traces, "GRPCNETCLIENT", "Comma-separated gRPC request metadata names to capture.", "grpc.request.metadata"),
        Option(54, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA", "OTEL_DOTNET_AUTO_TRACES_GRPCNETCLIENT_INSTRUMENTATION_CAPTURE_RESPONSE_METADATA", InstrumentationSignal.Traces, "GRPCNETCLIENT", "Comma-separated gRPC response metadata names to capture.", "grpc.response.metadata"),
        Option(55, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS", InstrumentationSignal.Traces, "HTTPCLIENT", "Comma-separated HTTP client request headers to capture.", "http.request.header"),
        Option(56, "instrumentation_options.OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", "OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS", InstrumentationSignal.Traces, "HTTPCLIENT", "Comma-separated HTTP client response headers to capture.", "http.response.header"),
        Option(57, "instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", "OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION", InstrumentationSignal.Traces, "ASPNETCORE", "Whether ASP.NET Core disables redaction for url.query.", "url.query"),
        Option(58, "instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", "OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION", InstrumentationSignal.Traces, "HTTPCLIENT", "Whether HTTP client disables redaction for url.full.", "url.full"),
        Option(59, "instrumentation_options.OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION", "OTEL_DOTNET_EXPERIMENTAL_ASPNET_DISABLE_URL_QUERY_REDACTION", InstrumentationSignal.Traces, "ASPNET", "Whether ASP.NET disables redaction for url.query.", "url.query"),
        Option(60, "instrumentation_options.OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED", "OTEL_DOTNET_AUTO_SQLCLIENT_NETFX_ILREWRITE_ENABLED", InstrumentationSignal.Traces, "SQLCLIENT", "NET Framework IL rewrite option from the upstream contract. NativeAOT runtime records the option but never performs IL rewriting.", "db.query.text|db.query.summary"),
    ];

    public static string EmitGeneratedManifestSource()
    {
        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("namespace Qyl.AutoInstrumentation.Generated;");
        builder.AppendLine();
        builder.AppendLine("internal static class QylGeneratedInstrumentationContract");
        builder.AppendLine("{");
        builder.AppendLine($"    public const int SignalSpecificInstrumentationPromiseCount = {SignalSpecificInstrumentationPromiseCount};");
        builder.AppendLine($"    public const int GlobalEnvironmentControlCount = {GlobalEnvironmentControlCount};");
        builder.AppendLine($"    public const int InstrumentationOptionCount = {InstrumentationOptionCount};");
        builder.AppendLine($"    public const int TotalCount = {TotalCount};");
        builder.AppendLine($"    public const int TracesSignalSpecificPromiseCount = {TracesSignalSpecificPromiseCount};");
        builder.AppendLine($"    public const int MetricsSignalSpecificPromiseCount = {MetricsSignalSpecificPromiseCount};");
        builder.AppendLine($"    public const int LogsSignalSpecificPromiseCount = {LogsSignalSpecificPromiseCount};");
        builder.AppendLine($"    public const int UniqueInstrumentationIdCount = {UniqueInstrumentationIdCount};");
        builder.AppendLine($"    public const int UnsupportedNativeAotSignalPromiseCount = {UnsupportedNativeAotSignalPromiseCount};");
        builder.AppendLine($"    public const int SourceGeneratedSignalPromiseCount = {SourceGeneratedSignalPromiseCount};");
        builder.AppendLine("    public const string AspNetCoreComponentsMeterName = \"Microsoft.AspNetCore.Components\";");
        builder.AppendLine("    public const string AspNetCoreComponentsNavigationMetricName = \"aspnetcore.components.navigation\";");
        builder.AppendLine();
        EmitStringArray(builder, "ItemIds", Items.Select(static item => item.Key));
        EmitStringArray(builder, "SignalKeys", Items.Where(static item => item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise).Select(static item => item.Key));
        EmitStringArray(builder, "SourceGeneratedSignalKeys", Items.Where(static item => item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise && !IsUnsupportedNativeAotSignalKey(item.Key)).Select(static item => item.Key));
        EmitStringArray(builder, "UnsupportedNativeAotSignalKeys", UnsupportedNativeAotSignalKeys);
        EmitStringArray(builder, "GlobalEnvironmentControls", Items.Where(static item => item.Kind is InstrumentationContractKind.GlobalEnvironmentControl).Select(static item => item.EnvironmentVariable));
        EmitStringArray(builder, "InstrumentationOptions", Items.Where(static item => item.Kind is InstrumentationContractKind.InstrumentationOption).Select(static item => item.EnvironmentVariable));
        builder.AppendLine("}");
        return builder.ToString();
    }

    public static InstrumentationContractItem? TryGetSourceGeneratedSignal(string key)
    {
        foreach (var item in Items)
        {
            if (item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise &&
                !IsUnsupportedNativeAotSignalKey(item.Key) &&
                string.Equals(item.Key, key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    public static InstrumentationContractItem? TryGetSupportedSignal(string key)
    {
        foreach (var item in Items)
        {
            if (item.Kind is InstrumentationContractKind.SignalSpecificInstrumentationPromise &&
                string.Equals(item.Key, key, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }

    private static bool IsUnsupportedNativeAotSignalKey(string key)
        => key is "signals.traces.ASPNET" or
            "signals.traces.WCFSERVICE" or
            "signals.metrics.ASPNET";

    private static InstrumentationContractItem Signal(
        int index,
        string key,
        InstrumentationSignal signal,
        string instrumentationId,
        string environmentVariable,
        string libraries,
        string supportedVersions,
        string instrumentationTypes,
        string promise)
        => new(
            index,
            ContractItemId(index),
            InstrumentationContractKind.SignalSpecificInstrumentationPromise,
            key,
            signal,
            instrumentationId,
            environmentVariable,
            supportedVersions,
            Split(libraries),
            Split(instrumentationTypes),
            promise,
            ImmutableArray<string>.Empty);

    private static InstrumentationContractItem Control(
        int index,
        string key,
        string environmentVariable,
        InstrumentationSignal signal,
        string promise)
        => new(
            index,
            ContractItemId(index),
            InstrumentationContractKind.GlobalEnvironmentControl,
            key,
            signal,
            string.Empty,
            environmentVariable,
            string.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            promise,
            ImmutableArray<string>.Empty);

    private static InstrumentationContractItem Option(
        int index,
        string key,
        string environmentVariable,
        InstrumentationSignal signal,
        string instrumentationId,
        string promise,
        string attributeNames)
        => new(
            index,
            ContractItemId(index),
            InstrumentationContractKind.InstrumentationOption,
            key,
            signal,
            instrumentationId,
            environmentVariable,
            string.Empty,
            ImmutableArray<string>.Empty,
            ImmutableArray<string>.Empty,
            promise,
            Split(attributeNames));

    private static ImmutableArray<string> Split(string value)
        => string.IsNullOrWhiteSpace(value)
            ? ImmutableArray<string>.Empty
            : value.Split('|').ToImmutableArray();

    private static string ContractItemId(int index)
        => "OTEL_DOTNET_AUTO_CONTRACT_" + index.ToString("000", System.Globalization.CultureInfo.InvariantCulture);

    private static void EmitStringArray(StringBuilder builder, string name, IEnumerable<string> values)
    {
        builder.Append("    public static string[] ");
        builder.Append(name);
        builder.AppendLine(" => new[]");
        builder.AppendLine("    {");

        foreach (var value in values)
        {
            builder.Append("        \"");
            builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
            builder.AppendLine("\",");
        }

        builder.AppendLine("    };");
        builder.AppendLine();
    }
}
