namespace Qyl.AutoInstrumentation.DiagnosticListeners.Semantics;

using Qyl.AutoInstrumentation;

internal static class SemanticAttributes
{
    public static readonly SemanticAttributeDefinition QylInstrumentationDomain = new(
        QylSemanticAttributes.QylInstrumentationDomain,
        SemanticStability.Development);

    public static readonly SemanticAttributeDefinition ErrorType = new(
        QylSemanticAttributes.ErrorType,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRequestMethod = new(
        QylSemanticAttributes.HttpRequestMethod,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRequestMethodOriginal = new(
        QylSemanticAttributes.HttpRequestMethodOriginal,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpResponseStatusCode = new(
        QylSemanticAttributes.HttpResponseStatusCode,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition HttpRoute = new(
        QylSemanticAttributes.HttpRoute,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition UrlFull = new(
        QylSemanticAttributes.UrlFull,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition UrlPath = new(
        QylSemanticAttributes.UrlPath,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition ServerAddress = new(
        QylSemanticAttributes.ServerAddress,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition ServerPort = new(
        QylSemanticAttributes.ServerPort,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbSystem = new(
        QylSemanticAttributes.DbSystemName,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbNamespace = new(
        QylSemanticAttributes.DbNamespace,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbOperationName = new(
        QylSemanticAttributes.DbOperationName,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbQuerySummary = new(
        QylSemanticAttributes.DbQuerySummary,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition DbQueryText = new(
        QylSemanticAttributes.DbQueryText,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcSystem = new(
        QylSemanticAttributes.RpcSystem,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcService = new(
        QylSemanticAttributes.RpcService,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcMethod = new(
        QylSemanticAttributes.RpcMethod,
        SemanticStability.Stable);

    public static readonly SemanticAttributeDefinition RpcGrpcStatusCode = new(
        QylSemanticAttributes.RpcGrpcStatusCode,
        SemanticStability.Development);
}
