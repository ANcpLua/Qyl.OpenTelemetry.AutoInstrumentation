namespace Qyl.AutoInstrumentation;

/// <summary>Composes the bounded, low-cardinality span names emitted by qyl auto-instrumentation.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery. Every input is already low-cardinality by construction: normalized HTTP methods, route templates, RPC method identifiers, and normalized database operation tokens.</remarks>
/// <example><code>var name = QylActivityNames.HttpClient("GET");</code></example>
public static class QylActivityNames
{
    private const string HttpFallback = "HTTP";
    private const string GrpcFallback = "gRPC";
    private const string DbFallback = "DB CLIENT";
    private const string SqlFallback = "SQL CLIENT";

    /// <summary>Composes the HTTP client span name: the normalized request method, or <c>HTTP</c> when the method is unknown.</summary>
    public static string HttpClient(string? normalizedMethod)
        => normalizedMethod is null or QylSemanticAttributes.HttpRequestMethodOther ? HttpFallback : normalizedMethod;

    /// <summary>Composes the HTTP server span name: <c>{method} {route}</c> when a route template exists, otherwise the method alone.</summary>
    public static string HttpServer(string? normalizedMethod, string? route)
    {
        var method = HttpClient(normalizedMethod);
        return string.IsNullOrEmpty(route) ? method : method + " " + route;
    }

    /// <summary>Composes the gRPC client span name: the full <c>{service}/{method}</c> RPC method name, or <c>gRPC</c> when unknown.</summary>
    public static string GrpcClient(string? service, string? method)
        => string.IsNullOrEmpty(service) || string.IsNullOrEmpty(method) ? GrpcFallback : service + "/" + method;

    /// <summary>Composes the database command span name: <c>DB {operation}</c>, or <c>DB CLIENT</c> when the operation is unknown.</summary>
    public static string DbCommand(string? operation)
        => string.IsNullOrEmpty(operation) ? DbFallback : "DB " + operation;

    /// <summary>Composes the SqlClient command span name: <c>SQL {operation}</c>, or <c>SQL CLIENT</c> when the operation is unknown.</summary>
    public static string SqlClientCommand(string? operation)
        => string.IsNullOrEmpty(operation) ? SqlFallback : "SQL " + operation;
}
