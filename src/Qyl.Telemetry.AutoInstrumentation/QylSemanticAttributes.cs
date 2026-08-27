namespace Qyl.Telemetry.AutoInstrumentation;

using DbAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Db.DbAttributes;
using QylIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using DbIncubatingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;
using CodeAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Code.CodeAttributes;
using ErrorAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using GraphqlAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes;
using HttpAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using MessagingAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using NetworkAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Network.NetworkAttributes;
using RpcAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.Telemetry.SemanticConventions.Attributes.Url.UrlAttributes;

internal static class QylSemanticAttributes
{
    public const string QylInstrumentationDomain = QylIncubatingAttributes.InstrumentationDomain;
    public const string HttpRequestMethod = HttpAttributes.RequestMethod;
    public const string HttpRequestMethodOriginal = HttpAttributes.RequestMethodOriginal;
    public const string HttpRequestMethodOther = HttpAttributes.RequestMethodValues.Other;
    public const string HttpRequestMethodConnect = HttpAttributes.RequestMethodValues.Connect;
    public const string HttpRequestMethodDelete = HttpAttributes.RequestMethodValues.Delete;
    public const string HttpRequestMethodGet = HttpAttributes.RequestMethodValues.Get;
    public const string HttpRequestMethodHead = HttpAttributes.RequestMethodValues.Head;
    public const string HttpRequestMethodOptions = HttpAttributes.RequestMethodValues.Options;
    public const string HttpRequestMethodPatch = HttpAttributes.RequestMethodValues.Patch;
    public const string HttpRequestMethodPost = HttpAttributes.RequestMethodValues.Post;
    public const string HttpRequestMethodPut = HttpAttributes.RequestMethodValues.Put;
    public const string HttpRequestMethodTrace = HttpAttributes.RequestMethodValues.Trace;
    public const string HttpResponseStatusCode = HttpAttributes.ResponseStatusCode;
    public const string HttpRequestHeaderPrefix = HttpAttributes.RequestHeader + ".";
    public const string HttpResponseHeaderPrefix = HttpAttributes.ResponseHeader + ".";
    public const string HttpRoute = HttpAttributes.Route;

    public const string UrlPath = UrlAttributes.Path;
    public const string UrlQuery = UrlAttributes.Query;
    public const string UrlFull = UrlAttributes.Full;
    public const string UrlScheme = UrlAttributes.Scheme;
    public const string CodeFunctionName = CodeAttributes.FunctionName;

    public const string DbSystemName = DbAttributes.SystemName;
    public const string DbNamespace = DbAttributes.Namespace;
    public const string DbCollectionName = DbIncubatingAttributes.CollectionName;
    public const string DbOperationName = DbAttributes.OperationName;
    public const string DbQuerySummary = DbAttributes.QuerySummary;
    public const string DbQueryText = DbAttributes.QueryText;
    public const string DbSystemElasticsearch = DbIncubatingAttributes.SystemNameValues.Elasticsearch;
    public const string DbSystemMicrosoftSqlServer = DbAttributes.SystemNameValues.MicrosoftSqlServer;
    public const string DbSystemMongodb = DbIncubatingAttributes.SystemNameValues.Mongodb;
    public const string DbSystemMysql = DbAttributes.SystemNameValues.Mysql;
    public const string DbSystemOracleDb = DbIncubatingAttributes.SystemNameValues.OracleDb;
    public const string DbSystemOtherSql = DbIncubatingAttributes.SystemNameValues.OtherSql;
    public const string DbSystemPostgresql = DbAttributes.SystemNameValues.Postgresql;
    public const string DbSystemRedis = DbIncubatingAttributes.SystemNameValues.Redis;
    public const string DbSystemSqlite = DbIncubatingAttributes.SystemNameValues.Sqlite;
    public const string DbSystemIbmDb2 = DbIncubatingAttributes.SystemNameValues.IbmDb2;

    public const string RpcSystem = RpcAttributes.SystemName;
    public const string RpcSystemGrpc = RpcAttributes.SystemNameValues.Grpc;
    public const string RpcSystemDotNetWcf = "dotnet_wcf";
    public const string RpcMethod = RpcAttributes.Method;
    public const string RpcMethodOriginal = RpcAttributes.MethodOriginal;
    public const string RpcResponseStatusCode = RpcAttributes.ResponseStatusCode;
    public const string GrpcRequestMetadataPrefix = RpcAttributes.RequestMetadata + ".";
    public const string GrpcResponseMetadataPrefix = RpcAttributes.ResponseMetadata + ".";

    public const string MessagingSystem = MessagingAttributes.System;
    public const string MessagingOperationName = MessagingAttributes.OperationName;
    public const string MessagingDestinationName = MessagingAttributes.DestinationName;
    public const string MessagingDestinationPartitionId = MessagingAttributes.DestinationPartitionId;
    public const string MessagingRabbitMqRoutingKey = MessagingAttributes.RabbitmqDestinationRoutingKey;
    public const string MessagingOperationType = MessagingAttributes.OperationType;
    public const string MessagingOperationTypeReceive = MessagingAttributes.OperationTypeValues.Receive;
    public const string MessagingOperationTypeSend = MessagingAttributes.OperationTypeValues.Send;
    public const string MessagingSystemKafka = MessagingAttributes.SystemValues.Kafka;
    public const string MessagingSystemRabbitMq = MessagingAttributes.SystemValues.Rabbitmq;
    public const string MessagingSystemMassTransit = "masstransit";
    public const string MessagingSystemNServiceBus = "nservicebus";

    public const string LogSeverity = "log.severity";
    public const string LogSeverityTrace = "Trace";
    public const string LogSeverityDebug = "Debug";
    public const string LogSeverityInformation = "Information";
    public const string LogSeverityWarning = "Warning";
    public const string LogSeverityError = "Error";
    public const string LogSeverityCritical = "Critical";
    public const string LogSeverityOther = "Other";
    public const string GraphQlOperationName = GraphqlAttributes.OperationName;
    public const string GraphQlDocument = GraphqlAttributes.Document;
    public const string GraphQlOperationType = GraphqlAttributes.OperationType;
    public const string GraphQlOperationTypeQuery = GraphqlAttributes.OperationTypeValues.Query;
    public const string GraphQlOperationTypeMutation = GraphqlAttributes.OperationTypeValues.Mutation;
    public const string GraphQlOperationTypeSubscription = GraphqlAttributes.OperationTypeValues.Subscription;

    public const string ServerAddress = ServerAttributes.Address;
    public const string ServerPort = ServerAttributes.Port;
    public const string NetworkPeerAddress = NetworkAttributes.PeerAddress;
    public const string NetworkPeerPort = NetworkAttributes.PeerPort;
    public const string NetworkProtocolVersion = NetworkAttributes.ProtocolVersion;
    public const string ErrorType = ErrorAttributes.Type;
}
