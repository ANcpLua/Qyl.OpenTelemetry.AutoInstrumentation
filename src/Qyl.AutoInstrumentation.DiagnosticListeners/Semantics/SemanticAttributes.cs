namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

internal static class SemanticAttributes
{
    public static readonly SemanticAttributeDefinition QylInstrumentationDomain = new(
        "qyl.instrumentation.domain",
        SemanticStability.Development);

    public static readonly SemanticAttributeDefinition ErrorType = new(
        "error.type",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRequestMethod = new(
        "http.request.method",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRequestMethodOriginal = new(
        "http.request.method_original",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpResponseStatusCode = new(
        "http.response.status_code",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRoute = new(
        "http.route",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition UrlFull = new(
        "url.full",
        SemanticStability.Stable,
        Sensitive: true);

    public static readonly SemanticAttributeDefinition UrlPath = new(
        "url.path",
        SemanticStability.Stable,
        Sensitive: true);

    public static readonly SemanticAttributeDefinition ServerAddress = new(
        "server.address",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition ServerPort = new(
        "server.port",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbSystem = new(
        "db.system",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbNamespace = new(
        "db.namespace",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbOperationName = new(
        "db.operation.name",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbQuerySummary = new(
        "db.query.summary",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbQueryText = new(
        "db.query.text",
        SemanticStability.Stable,
        Sensitive: true);

    public static readonly SemanticAttributeDefinition RpcSystem = new(
        "rpc.system",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcService = new(
        "rpc.service",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcMethod = new(
        "rpc.method",
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcGrpcStatusCode = new(
        "rpc.grpc.status_code",
        SemanticStability.Development);
}
