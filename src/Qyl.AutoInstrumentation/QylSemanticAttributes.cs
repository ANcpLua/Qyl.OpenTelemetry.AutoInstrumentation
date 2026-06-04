namespace Qyl.AutoInstrumentation;

using DbAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes;
using ErrorAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using ExceptionAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Exception.ExceptionAttributes;
using GraphqlAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes;
using HttpAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using MessagingAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using OtelAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Otel.OtelAttributes;
using RpcAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes;

public static class QylSemanticAttributes
{
    public const string QylInstrumentationDomain = "qyl.instrumentation.domain";

    public const string HttpRequestMethod = HttpAttributes.RequestMethod;
    public const string HttpRequestMethodOriginal = HttpAttributes.RequestMethodOriginal;
    public const string HttpResponseStatusCode = HttpAttributes.ResponseStatusCode;
    public const string HttpRequestHeaderPrefix = HttpAttributes.RequestHeader + ".";
    public const string HttpResponseHeaderPrefix = HttpAttributes.ResponseHeader + ".";
    public const string HttpRoute = HttpAttributes.Route;

    public const string UrlPath = UrlAttributes.Path;
    public const string UrlQuery = UrlAttributes.Query;
    public const string UrlFull = UrlAttributes.Full;

    public const string DbSystemName = DbAttributes.SystemName;
    public const string DbNamespace = DbAttributes.Namespace;
    public const string DbOperationName = DbAttributes.OperationName;
    public const string DbQuerySummary = DbAttributes.QuerySummary;
    public const string DbQueryText = DbAttributes.QueryText;
    public const string DbSystemMicrosoftSqlServer = DbAttributes.SystemNameValues.MicrosoftSqlServer;
    public const string DbSystemMysql = DbAttributes.SystemNameValues.Mysql;
    public const string DbSystemOracleDb = DbAttributes.SystemNameValues.OracleDb;
    public const string DbSystemOtherSql = DbAttributes.SystemNameValues.OtherSql;
    public const string DbSystemPostgresql = DbAttributes.SystemNameValues.Postgresql;
    public const string DbSystemSqlite = DbAttributes.SystemNameValues.Sqlite;

#pragma warning disable CS0618 // Qyl still mirrors the current OTEL .NET auto gRPC status attribute contract.
    public const string RpcSystem = RpcAttributes.System;
    public const string RpcService = RpcAttributes.Service;
    public const string RpcMethod = RpcAttributes.Method;
    public const string RpcGrpcStatusCode = RpcAttributes.GrpcStatusCode;
#pragma warning restore CS0618
    public const string GrpcRequestMetadataPrefix = RpcAttributes.RequestMetadata + ".";
    public const string GrpcResponseMetadataPrefix = RpcAttributes.ResponseMetadata + ".";

    public const string MessagingSystem = MessagingAttributes.System;
    public const string MessagingOperationName = MessagingAttributes.OperationName;
    public const string MessagingDestinationName = MessagingAttributes.DestinationName;

    public const string LogSeverity = "log.severity";
    public const string LogEventName = OtelAttributes.EventName;

    public const string GraphQlOperationName = GraphqlAttributes.OperationName;
    public const string GraphQlDocument = GraphqlAttributes.Document;

    public const string ServerAddress = ServerAttributes.Address;
    public const string ServerPort = ServerAttributes.Port;
    public const string ErrorType = ErrorAttributes.Type;
    public const string ExceptionType = ExceptionAttributes.Type;
}
