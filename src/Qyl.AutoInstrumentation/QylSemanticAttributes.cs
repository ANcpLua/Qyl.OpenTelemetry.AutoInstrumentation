namespace Qyl.AutoInstrumentation;

internal static class QylSemanticAttributes
{
    public const string QylInstrumentationDomain = "qyl.instrumentation.domain";
    public const string HttpRequestMethod = "http.request.method";
    public const string HttpResponseStatusCode = "http.response.status_code";
    public const string HttpRequestHeaderPrefix = "http.request.header.";
    public const string HttpResponseHeaderPrefix = "http.response.header.";
    public const string HttpRoute = "http.route";
    public const string UrlPath = "url.path";
    public const string DbSystemName = "db.system.name";
    public const string DbNamespace = "db.namespace";
    public const string DbOperationName = "db.operation.name";
    public const string DbQuerySummary = "db.query.summary";
    public const string DbQueryText = "db.query.text";
    public const string RpcSystem = "rpc.system";
    public const string RpcService = "rpc.service";
    public const string RpcMethod = "rpc.method";
    public const string RpcGrpcStatusCode = "rpc.grpc.status_code";
    public const string MessagingSystem = "messaging.system";
    public const string MessagingOperationName = "messaging.operation.name";
    public const string MessagingDestinationName = "messaging.destination.name";
    public const string LogSeverity = "log.severity";
    public const string LogEventName = "log.event.name";
    public const string GraphQlOperationName = "graphql.operation.name";
    public const string ServerAddress = "server.address";
    public const string ServerPort = "server.port";
    public const string UrlFull = "url.full";
    public const string ErrorType = "error.type";
}
