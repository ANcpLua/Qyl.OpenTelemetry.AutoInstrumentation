namespace Qyl.OpenTelemetry.AutoInstrumentation;

using DbAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Db.DbAttributes;
using DbIncubatingAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Db.DbAttributes;
using CpuAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Cpu.CpuAttributes;
using DotnetAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Dotnet.DotnetAttributes;
using ErrorAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes;
using ExceptionAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Exception.ExceptionAttributes;
using GraphqlAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes;
using HttpAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Http.HttpAttributes;
using MessagingAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Messaging.MessagingAttributes;
using OtelAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Otel.OtelAttributes;
using RpcAttributes = Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Rpc.RpcAttributes;
using ServerAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Server.ServerAttributes;
using UrlAttributes = Qyl.OpenTelemetry.SemanticConventions.Attributes.Url.UrlAttributes;

/// <summary>Defines the qyl auto-instrumentation surface for qyl Semantic Attributes.</summary>
/// <remarks>This runtime surface is NativeAOT-compatible and is consumed by source-generated interceptors without runtime IL rewriting, profiler attach, or reflection discovery.</remarks>
public static class QylSemanticAttributes
{
    /// <summary>Well-known qyl Instrumentation Domain value used by qyl auto-instrumentation.</summary>
    public const string QylInstrumentationDomain = "qyl.instrumentation.domain";
    /// <summary>Well-known HTTP Request Method value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethod = HttpAttributes.RequestMethod;
    /// <summary>Well-known HTTP Request Method Original value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodOriginal = HttpAttributes.RequestMethodOriginal;
    /// <summary>Well-known HTTP Request Method Other value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodOther = HttpAttributes.RequestMethodValues.Other;
    /// <summary>Well-known HTTP Request Method Connect value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodConnect = HttpAttributes.RequestMethodValues.Connect;
    /// <summary>Well-known HTTP Request Method Delete value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodDelete = HttpAttributes.RequestMethodValues.Delete;
    /// <summary>Well-known HTTP Request Method Get value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodGet = HttpAttributes.RequestMethodValues.Get;
    /// <summary>Well-known HTTP Request Method Head value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodHead = HttpAttributes.RequestMethodValues.Head;
    /// <summary>Well-known HTTP Request Method Options value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodOptions = HttpAttributes.RequestMethodValues.Options;
    /// <summary>Well-known HTTP Request Method Patch value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodPatch = HttpAttributes.RequestMethodValues.Patch;
    /// <summary>Well-known HTTP Request Method Post value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodPost = HttpAttributes.RequestMethodValues.Post;
    /// <summary>Well-known HTTP Request Method Put value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodPut = HttpAttributes.RequestMethodValues.Put;
    /// <summary>Well-known HTTP Request Method Trace value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestMethodTrace = HttpAttributes.RequestMethodValues.Trace;
    /// <summary>Well-known HTTP Response Status Code value used by qyl auto-instrumentation.</summary>
    public const string HttpResponseStatusCode = HttpAttributes.ResponseStatusCode;
    /// <summary>Well-known HTTP Request Header Prefix value used by qyl auto-instrumentation.</summary>
    public const string HttpRequestHeaderPrefix = HttpAttributes.RequestHeader + ".";
    /// <summary>Well-known HTTP Response Header Prefix value used by qyl auto-instrumentation.</summary>
    public const string HttpResponseHeaderPrefix = HttpAttributes.ResponseHeader + ".";
    /// <summary>Well-known HTTP Route value used by qyl auto-instrumentation.</summary>
    public const string HttpRoute = HttpAttributes.Route;

    /// <summary>Well-known Url Path value used by qyl auto-instrumentation.</summary>
    public const string UrlPath = UrlAttributes.Path;
    /// <summary>Well-known Url Query value used by qyl auto-instrumentation.</summary>
    public const string UrlQuery = UrlAttributes.Query;
    /// <summary>Well-known Url Full value used by qyl auto-instrumentation.</summary>
    public const string UrlFull = UrlAttributes.Full;
    /// <summary>Well-known Url Scheme value used by qyl auto-instrumentation.</summary>
    public const string UrlScheme = "url.scheme";
    /// <summary>Well-known Code Function Name value used by qyl auto-instrumentation.</summary>
    public const string CodeFunctionName = "code.function.name";

    /// <summary>Well-known Dotnet Gc Heap Generation value used by qyl auto-instrumentation.</summary>
    public const string DotnetGcHeapGeneration = DotnetAttributes.GcHeapGeneration;
    /// <summary>Well-known Dotnet Gc Heap Generation Gen0 value used by qyl auto-instrumentation.</summary>
    public const string DotnetGcHeapGenerationGen0 = DotnetAttributes.GcHeapGenerationValues.Gen0;
    /// <summary>Well-known Dotnet Gc Heap Generation Gen1 value used by qyl auto-instrumentation.</summary>
    public const string DotnetGcHeapGenerationGen1 = DotnetAttributes.GcHeapGenerationValues.Gen1;
    /// <summary>Well-known Dotnet Gc Heap Generation Gen2 value used by qyl auto-instrumentation.</summary>
    public const string DotnetGcHeapGenerationGen2 = DotnetAttributes.GcHeapGenerationValues.Gen2;

    /// <summary>Well-known Cpu Mode value used by qyl auto-instrumentation.</summary>
    public const string CpuMode = CpuAttributes.Mode;
    /// <summary>Well-known Cpu Mode System value used by qyl auto-instrumentation.</summary>
    public const string CpuModeSystem = CpuAttributes.ModeValues.System;
    /// <summary>Well-known Cpu Mode User value used by qyl auto-instrumentation.</summary>
    public const string CpuModeUser = CpuAttributes.ModeValues.User;

    /// <summary>Well-known database System Name value used by qyl auto-instrumentation.</summary>
    public const string DbSystemName = DbAttributes.SystemName;
    /// <summary>Well-known database Namespace value used by qyl auto-instrumentation.</summary>
    public const string DbNamespace = DbAttributes.Namespace;
    /// <summary>Well-known database Operation Name value used by qyl auto-instrumentation.</summary>
    public const string DbOperationName = DbAttributes.OperationName;
    /// <summary>Well-known database Query Summary value used by qyl auto-instrumentation.</summary>
    public const string DbQuerySummary = DbAttributes.QuerySummary;
    /// <summary>Well-known database Query Text value used by qyl auto-instrumentation.</summary>
    public const string DbQueryText = DbAttributes.QueryText;
    /// <summary>Well-known database System Elasticsearch value used by qyl auto-instrumentation.</summary>
    public const string DbSystemElasticsearch = DbIncubatingAttributes.SystemNameValues.Elasticsearch;
    /// <summary>Well-known database System Microsoft Sql Server value used by qyl auto-instrumentation.</summary>
    public const string DbSystemMicrosoftSqlServer = DbAttributes.SystemNameValues.MicrosoftSqlServer;
    /// <summary>Well-known database System Mongodb value used by qyl auto-instrumentation.</summary>
    public const string DbSystemMongodb = DbIncubatingAttributes.SystemNameValues.Mongodb;
    /// <summary>Well-known database System Mysql value used by qyl auto-instrumentation.</summary>
    public const string DbSystemMysql = DbAttributes.SystemNameValues.Mysql;
    /// <summary>Well-known database System Oracle Db value used by qyl auto-instrumentation.</summary>
    public const string DbSystemOracleDb = DbIncubatingAttributes.SystemNameValues.OracleDb;
    /// <summary>Well-known database System Other Sql value used by qyl auto-instrumentation.</summary>
    public const string DbSystemOtherSql = DbIncubatingAttributes.SystemNameValues.OtherSql;
    /// <summary>Well-known database System Postgresql value used by qyl auto-instrumentation.</summary>
    public const string DbSystemPostgresql = DbAttributes.SystemNameValues.Postgresql;
    /// <summary>Well-known database System Redis value used by qyl auto-instrumentation.</summary>
    public const string DbSystemRedis = DbIncubatingAttributes.SystemNameValues.Redis;
    /// <summary>Well-known database System Sqlite value used by qyl auto-instrumentation.</summary>
    public const string DbSystemSqlite = DbIncubatingAttributes.SystemNameValues.Sqlite;

    /// <summary>Well-known Rpc System value used by qyl auto-instrumentation.</summary>
    public const string RpcSystem = RpcAttributes.SystemName;
    /// <summary>Well-known Rpc System Grpc value used by qyl auto-instrumentation.</summary>
    public const string RpcSystemGrpc = RpcAttributes.SystemNameValues.Grpc;
#pragma warning disable CS0618 // DotnetWcf exists only on the deprecated value set in the current semconv package.
    /// <summary>Well-known Rpc System Dot Net Wcf value used by qyl auto-instrumentation.</summary>
    public const string RpcSystemDotNetWcf = RpcAttributes.SystemValues.DotnetWcf;
#pragma warning restore CS0618
#pragma warning disable CS0618 // Qyl still mirrors the current OTEL .NET auto gRPC status attribute contract.
    /// <summary>Well-known Rpc Service value used by qyl auto-instrumentation.</summary>
    public const string RpcService = RpcAttributes.Service;
    /// <summary>Well-known Rpc Method value used by qyl auto-instrumentation.</summary>
    public const string RpcMethod = RpcAttributes.Method;
    /// <summary>Well-known Rpc gRPC Status Code value used by qyl auto-instrumentation.</summary>
    public const string RpcGrpcStatusCode = RpcAttributes.GrpcStatusCode;
    /// <summary>Well-known Rpc gRPC Status Code Ok value used by qyl auto-instrumentation.</summary>
    public static readonly int RpcGrpcStatusCodeOk = GetRpcGrpcStatusCodeOk();
#pragma warning restore CS0618
    /// <summary>Well-known gRPC Request Metadata Prefix value used by qyl auto-instrumentation.</summary>
    public const string GrpcRequestMetadataPrefix = RpcAttributes.RequestMetadata + ".";
    /// <summary>Well-known gRPC Response Metadata Prefix value used by qyl auto-instrumentation.</summary>
    public const string GrpcResponseMetadataPrefix = RpcAttributes.ResponseMetadata + ".";

    /// <summary>Well-known Messaging System value used by qyl auto-instrumentation.</summary>
    public const string MessagingSystem = MessagingAttributes.System;
    /// <summary>Well-known Messaging Operation Name value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationName = MessagingAttributes.OperationName;
    /// <summary>Well-known Messaging Operation Name Publish value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationNamePublish = "publish";
    /// <summary>Well-known Messaging Operation Name Send value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationNameSend = MessagingAttributes.OperationTypeValues.Send;
    /// <summary>Well-known Messaging Operation Type value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationType = MessagingAttributes.OperationType;
    /// <summary>Well-known Messaging Operation Type Receive value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationTypeReceive = MessagingAttributes.OperationTypeValues.Receive;
    /// <summary>Well-known Messaging Operation Type Send value used by qyl auto-instrumentation.</summary>
    public const string MessagingOperationTypeSend = MessagingAttributes.OperationTypeValues.Send;
    /// <summary>Well-known Messaging System Kafka value used by qyl auto-instrumentation.</summary>
    public const string MessagingSystemKafka = MessagingAttributes.SystemValues.Kafka;
    /// <summary>Well-known Messaging System Rabbit Mq value used by qyl auto-instrumentation.</summary>
    public const string MessagingSystemRabbitMq = MessagingAttributes.SystemValues.Rabbitmq;
    /// <summary>Well-known Messaging System Mass Transit value used by qyl auto-instrumentation.</summary>
    public const string MessagingSystemMassTransit = "masstransit";
    /// <summary>Well-known Messaging System N Service Bus value used by qyl auto-instrumentation.</summary>
    public const string MessagingSystemNServiceBus = "nservicebus";

    /// <summary>Well-known Log Severity value used by qyl auto-instrumentation.</summary>
    public const string LogSeverity = "log.severity";
    /// <summary>Well-known Log Severity Trace value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityTrace = "Trace";
    /// <summary>Well-known Log Severity Debug value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityDebug = "Debug";
    /// <summary>Well-known Log Severity Information value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityInformation = "Information";
    /// <summary>Well-known Log Severity Warning value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityWarning = "Warning";
    /// <summary>Well-known Log Severity Error value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityError = "Error";
    /// <summary>Well-known Log Severity Critical value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityCritical = "Critical";
    /// <summary>Well-known Log Severity Other value used by qyl auto-instrumentation.</summary>
    public const string LogSeverityOther = "Other";
    /// <summary>Well-known Log Event Name value used by qyl auto-instrumentation.</summary>
    public const string LogEventName = OtelAttributes.EventName;

    /// <summary>Well-known Graph Ql Operation Name value used by qyl auto-instrumentation.</summary>
    public const string GraphQlOperationName = GraphqlAttributes.OperationName;
    /// <summary>Well-known Graph Ql Document value used by qyl auto-instrumentation.</summary>
    public const string GraphQlDocument = GraphqlAttributes.Document;

    /// <summary>Well-known Server Address value used by qyl auto-instrumentation.</summary>
    public const string ServerAddress = ServerAttributes.Address;
    /// <summary>Well-known Server Port value used by qyl auto-instrumentation.</summary>
    public const string ServerPort = ServerAttributes.Port;
    /// <summary>Well-known Error Type value used by qyl auto-instrumentation.</summary>
    public const string ErrorType = ErrorAttributes.Type;
    /// <summary>Well-known Exception Type value used by qyl auto-instrumentation.</summary>
    public const string ExceptionType = ExceptionAttributes.Type;

    private static int GetRpcGrpcStatusCodeOk()
        => global::System.Int32.TryParse(
            RpcAttributes.GrpcStatusCodeValues.Ok,
            global::System.Globalization.NumberStyles.Integer,
            global::System.Globalization.CultureInfo.InvariantCulture,
            out var value)
            ? value
            : default;
}
