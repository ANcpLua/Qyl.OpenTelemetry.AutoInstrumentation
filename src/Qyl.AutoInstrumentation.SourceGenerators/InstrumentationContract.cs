using System.Collections.Immutable;
using System.Text;

namespace Qyl.AutoInstrumentation.SourceGenerators;

internal enum InstrumentationContractKind
{
    Signal,
    EnvironmentControl,
    Option,
}

internal readonly record struct InstrumentationContractItem(
    string Id,
    InstrumentationContractKind Kind,
    string Emitter,
    string Promise);

internal static class InstrumentationContract
{
    public const int SignalSpecificInstrumentationPromiseCount = 37;
    public const int GlobalEnvironmentControlCount = 7;
    public const int InstrumentationOptionCount = 16;
    public const int TotalCount =
        SignalSpecificInstrumentationPromiseCount +
        GlobalEnvironmentControlCount +
        InstrumentationOptionCount;

    public static readonly ImmutableArray<InstrumentationContractItem> Items =
    [
        new("http.client.send_async", InstrumentationContractKind.Signal, "HttpClientSendAsyncEmitter", "Intercept source-visible HttpClient.SendAsync calls without changing exception semantics."),
        new("http.client.get_async", InstrumentationContractKind.Signal, "HttpClientGetAsyncEmitter", "Intercept source-visible HttpClient.GetAsync calls."),
        new("http.client.post_async", InstrumentationContractKind.Signal, "HttpClientPostAsyncEmitter", "Intercept source-visible HttpClient.PostAsync calls."),
        new("http.client.put_async", InstrumentationContractKind.Signal, "HttpClientPutAsyncEmitter", "Intercept source-visible HttpClient.PutAsync calls."),
        new("http.client.delete_async", InstrumentationContractKind.Signal, "HttpClientDeleteAsyncEmitter", "Intercept source-visible HttpClient.DeleteAsync calls."),
        new("http.client.patch_async", InstrumentationContractKind.Signal, "HttpClientPatchAsyncEmitter", "Intercept source-visible HttpClient.PatchAsync calls."),
        new("http.client.get_string_async", InstrumentationContractKind.Signal, "HttpClientGetStringAsyncEmitter", "Intercept source-visible HttpClient.GetStringAsync calls."),
        new("http.client.get_byte_array_async", InstrumentationContractKind.Signal, "HttpClientGetByteArrayAsyncEmitter", "Intercept source-visible HttpClient.GetByteArrayAsync calls."),
        new("http.client.get_stream_async", InstrumentationContractKind.Signal, "HttpClientGetStreamAsyncEmitter", "Intercept source-visible HttpClient.GetStreamAsync calls."),
        new("http.client.send", InstrumentationContractKind.Signal, "HttpClientSendEmitter", "Intercept source-visible synchronous HttpClient.Send calls."),
        new("aspnetcore.endpoint.map_get", InstrumentationContractKind.Signal, "AspNetCoreMapGetEmitter", "Intercept source-visible ASP.NET Core MapGet endpoint registrations."),
        new("aspnetcore.endpoint.map_post", InstrumentationContractKind.Signal, "AspNetCoreMapPostEmitter", "Intercept source-visible ASP.NET Core MapPost endpoint registrations."),
        new("aspnetcore.endpoint.map_put", InstrumentationContractKind.Signal, "AspNetCoreMapPutEmitter", "Intercept source-visible ASP.NET Core MapPut endpoint registrations."),
        new("aspnetcore.endpoint.map_delete", InstrumentationContractKind.Signal, "AspNetCoreMapDeleteEmitter", "Intercept source-visible ASP.NET Core MapDelete endpoint registrations."),
        new("aspnetcore.endpoint.map_patch", InstrumentationContractKind.Signal, "AspNetCoreMapPatchEmitter", "Intercept source-visible ASP.NET Core MapPatch endpoint registrations."),
        new("aspnetcore.request_delegate.invoke", InstrumentationContractKind.Signal, "AspNetCoreRequestDelegateEmitter", "Intercept source-visible RequestDelegate.Invoke calls."),
        new("efcore.query.to_list_async", InstrumentationContractKind.Signal, "EfCoreToListAsyncEmitter", "Intercept source-visible EF Core ToListAsync calls."),
        new("efcore.query.first_or_default_async", InstrumentationContractKind.Signal, "EfCoreFirstOrDefaultAsyncEmitter", "Intercept source-visible EF Core FirstOrDefaultAsync calls."),
        new("efcore.query.single_or_default_async", InstrumentationContractKind.Signal, "EfCoreSingleOrDefaultAsyncEmitter", "Intercept source-visible EF Core SingleOrDefaultAsync calls."),
        new("efcore.query.count_async", InstrumentationContractKind.Signal, "EfCoreCountAsyncEmitter", "Intercept source-visible EF Core CountAsync calls."),
        new("efcore.dbcontext.save_changes", InstrumentationContractKind.Signal, "EfCoreSaveChangesEmitter", "Intercept source-visible DbContext.SaveChanges calls."),
        new("efcore.dbcontext.save_changes_async", InstrumentationContractKind.Signal, "EfCoreSaveChangesAsyncEmitter", "Intercept source-visible DbContext.SaveChangesAsync calls."),
        new("sqlclient.command.execute_reader", InstrumentationContractKind.Signal, "SqlClientExecuteReaderEmitter", "Intercept source-visible DbCommand.ExecuteReader calls."),
        new("sqlclient.command.execute_reader_async", InstrumentationContractKind.Signal, "SqlClientExecuteReaderAsyncEmitter", "Intercept source-visible DbCommand.ExecuteReaderAsync calls."),
        new("sqlclient.command.execute_scalar", InstrumentationContractKind.Signal, "SqlClientExecuteScalarEmitter", "Intercept source-visible DbCommand.ExecuteScalar calls."),
        new("sqlclient.command.execute_scalar_async", InstrumentationContractKind.Signal, "SqlClientExecuteScalarAsyncEmitter", "Intercept source-visible DbCommand.ExecuteScalarAsync calls."),
        new("sqlclient.command.execute_non_query", InstrumentationContractKind.Signal, "SqlClientExecuteNonQueryEmitter", "Intercept source-visible DbCommand.ExecuteNonQuery calls."),
        new("sqlclient.command.execute_non_query_async", InstrumentationContractKind.Signal, "SqlClientExecuteNonQueryAsyncEmitter", "Intercept source-visible DbCommand.ExecuteNonQueryAsync calls."),
        new("grpc.client.async_unary_call", InstrumentationContractKind.Signal, "GrpcAsyncUnaryCallEmitter", "Intercept source-visible gRPC AsyncUnaryCall calls."),
        new("grpc.client.async_server_streaming_call", InstrumentationContractKind.Signal, "GrpcAsyncServerStreamingCallEmitter", "Intercept source-visible gRPC AsyncServerStreamingCall calls."),
        new("grpc.client.async_client_streaming_call", InstrumentationContractKind.Signal, "GrpcAsyncClientStreamingCallEmitter", "Intercept source-visible gRPC AsyncClientStreamingCall calls."),
        new("grpc.client.async_duplex_streaming_call", InstrumentationContractKind.Signal, "GrpcAsyncDuplexStreamingCallEmitter", "Intercept source-visible gRPC AsyncDuplexStreamingCall calls."),
        new("messaging.kafka.producer.produce_async", InstrumentationContractKind.Signal, "KafkaProduceAsyncEmitter", "Intercept source-visible Kafka producer ProduceAsync calls."),
        new("messaging.kafka.consumer.consume", InstrumentationContractKind.Signal, "KafkaConsumeEmitter", "Intercept source-visible Kafka consumer Consume calls."),
        new("redis.database.string_get_async", InstrumentationContractKind.Signal, "RedisStringGetAsyncEmitter", "Intercept source-visible Redis StringGetAsync calls."),
        new("genai.openai.chat.create_async", InstrumentationContractKind.Signal, "OpenAiChatCreateAsyncEmitter", "Intercept source-visible OpenAI chat creation calls."),
        new("mcp.client.call_tool_async", InstrumentationContractKind.Signal, "McpCallToolAsyncEmitter", "Intercept source-visible MCP client CallToolAsync calls."),

        new("env.enabled", InstrumentationContractKind.EnvironmentControl, "EnabledEnvironmentControl", "QYL_AUTOINSTRUMENTATION_ENABLED gates all generated interception."),
        new("env.service_name", InstrumentationContractKind.EnvironmentControl, "ServiceNameEnvironmentControl", "QYL_AUTOINSTRUMENTATION_SERVICE_NAME overrides detected service name."),
        new("env.export_endpoint", InstrumentationContractKind.EnvironmentControl, "ExportEndpointEnvironmentControl", "QYL_AUTOINSTRUMENTATION_EXPORT_ENDPOINT configures the exporter endpoint."),
        new("env.capture_sensitive_values", InstrumentationContractKind.EnvironmentControl, "CaptureSensitiveValuesEnvironmentControl", "QYL_AUTOINSTRUMENTATION_CAPTURE_SENSITIVE_VALUES is off by default."),
        new("env.disabled_instrumentations", InstrumentationContractKind.EnvironmentControl, "DisabledInstrumentationsEnvironmentControl", "QYL_AUTOINSTRUMENTATION_DISABLED_INSTRUMENTATIONS disables selected emitters."),
        new("env.sample_rate", InstrumentationContractKind.EnvironmentControl, "SampleRateEnvironmentControl", "QYL_AUTOINSTRUMENTATION_SAMPLE_RATE bounds generated telemetry volume."),
        new("env.strict_semconv", InstrumentationContractKind.EnvironmentControl, "StrictSemconvEnvironmentControl", "QYL_AUTOINSTRUMENTATION_STRICT_SEMCONV fails gates when emitted keys drift."),

        new("option.http.capture_request_headers", InstrumentationContractKind.Option, "HttpCaptureRequestHeadersOption", "HTTP request header capture is explicit opt-in."),
        new("option.http.capture_response_headers", InstrumentationContractKind.Option, "HttpCaptureResponseHeadersOption", "HTTP response header capture is explicit opt-in."),
        new("option.http.capture_url_full", InstrumentationContractKind.Option, "HttpCaptureUrlFullOption", "url.full capture is explicit opt-in."),
        new("option.http.excluded_hosts", InstrumentationContractKind.Option, "HttpExcludedHostsOption", "HTTP host exclusions suppress selected client spans."),
        new("option.db.capture_statement", InstrumentationContractKind.Option, "DbCaptureStatementOption", "DB statement capture is explicit opt-in."),
        new("option.db.statement_max_length", InstrumentationContractKind.Option, "DbStatementMaxLengthOption", "DB statement capture is length-bounded."),
        new("option.db.capture_parameters", InstrumentationContractKind.Option, "DbCaptureParametersOption", "DB parameter capture is explicit opt-in."),
        new("option.rpc.capture_metadata", InstrumentationContractKind.Option, "RpcCaptureMetadataOption", "RPC metadata capture is explicit opt-in."),
        new("option.messaging.capture_payload_size", InstrumentationContractKind.Option, "MessagingCapturePayloadSizeOption", "Messaging payload size capture records bounded numeric size only."),
        new("option.genai.capture_prompts", InstrumentationContractKind.Option, "GenAiCapturePromptsOption", "GenAI prompt capture is explicit opt-in."),
        new("option.genai.capture_completions", InstrumentationContractKind.Option, "GenAiCaptureCompletionsOption", "GenAI completion capture is explicit opt-in."),
        new("option.mcp.capture_arguments", InstrumentationContractKind.Option, "McpCaptureArgumentsOption", "MCP argument capture is explicit opt-in."),
        new("option.metrics.enabled", InstrumentationContractKind.Option, "MetricsEnabledOption", "Generated metric emission can be disabled independently."),
        new("option.traces.enabled", InstrumentationContractKind.Option, "TracesEnabledOption", "Generated trace emission can be disabled independently."),
        new("option.logs.enabled", InstrumentationContractKind.Option, "LogsEnabledOption", "Generated log emission can be disabled independently."),
        new("option.exceptions.stacktrace_enabled", InstrumentationContractKind.Option, "ExceptionStackTraceEnabledOption", "Exception stack traces are explicit opt-in."),
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
        builder.AppendLine();
        builder.AppendLine("    public static string[] ItemIds => new[]");
        builder.AppendLine("    {");

        foreach (var item in Items)
            builder.AppendLine($"        \"{item.Id}\",");

        builder.AppendLine("    };");
        builder.AppendLine("}");
        return builder.ToString();
    }

    public static InstrumentationContractItem? TryGetSupportedSignal(string id)
    {
        foreach (var item in Items)
        {
            if (item.Kind is InstrumentationContractKind.Signal &&
                string.Equals(item.Id, id, StringComparison.Ordinal))
            {
                return item;
            }
        }

        return null;
    }
}
