namespace Qyl.Telemetry.AutoInstrumentation.Internal;

/// <summary>
/// Span names derived from the span's own low-cardinality attributes, following the naming rule
/// of each OpenTelemetry signal family.
/// </summary>
internal static class QylSpanNames
{
    private const string HttpFallback = "HTTP";
    private const string GrpcFallback = "gRPC";
    private const string GraphQlFallback = "GraphQL Operation";
    private const string JobFallback = "job";

    public static string Http(string? method)
        => method is null or QylSemanticAttributes.HttpRequestMethodOther ? HttpFallback : method;

    public static string HttpServer(string? method, string? route)
    {
        var name = Http(method);
        return string.IsNullOrEmpty(route) ? name : name + " " + route;
    }

    public static string Grpc(string? method)
        => string.IsNullOrWhiteSpace(method) || StringComparer.Ordinal.Equals(method, QylGrpcSemantics.OtherMethod)
            ? GrpcFallback
            : method;

    public static string Db(string? summary, string systemName)
        => string.IsNullOrEmpty(summary) ? systemName : summary;

    public static string Messaging(string operationName, string? destination)
        => string.IsNullOrEmpty(destination) ? operationName : operationName + " " + destination;

    public static string GraphQl(string? operationType)
        => operationType ?? GraphQlFallback;

    public static string Rpc(string? method, string systemName)
        => string.IsNullOrEmpty(method) ? systemName : method;

    public static string Job(string? group, string? name)
    {
        var qualifier = string.IsNullOrEmpty(group) ? string.Empty : group + ".";
        return string.IsNullOrEmpty(name) ? JobFallback : qualifier + name;
    }
}
