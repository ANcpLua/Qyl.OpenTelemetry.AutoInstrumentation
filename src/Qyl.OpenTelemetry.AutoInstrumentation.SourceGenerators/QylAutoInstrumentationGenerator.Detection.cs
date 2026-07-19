using ANcpLua.Roslyn.Utilities;
using Microsoft.CodeAnalysis;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private static bool TryGetHttpClientInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!IsType(symbol.ContainingType, "global::System.Net.Http.HttpClient"))
            return false;

        if (string.Equals(symbol.Name, "Send", StringComparison.Ordinal) &&
            IsType(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetSendShape(symbol, out var parameters))
        {
            target = HttpTarget(symbol, "Send", "global::System.Net.Http.HttpResponseMessage", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "SendAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetSendShape(symbol, out parameters))
        {
            target = HttpTarget(symbol, "SendAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "GetAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetRequestUriShape(symbol, allowCompletionOption: true, out parameters))
        {
            target = HttpTarget(symbol, "GetAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "DeleteAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetRequestUriShape(symbol, allowCompletionOption: false, out parameters))
        {
            target = HttpTarget(symbol, "DeleteAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "PostAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetRequestUriContentShape(symbol, out parameters))
        {
            target = HttpTarget(symbol, "PostAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "PutAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetRequestUriContentShape(symbol, out parameters))
        {
            target = HttpTarget(symbol, "PutAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "PatchAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Net.Http.HttpResponseMessage") &&
            TryGetRequestUriContentShape(symbol, out parameters))
        {
            target = HttpTarget(symbol, "PatchAsync", "global::System.Threading.Tasks.Task<global::System.Net.Http.HttpResponseMessage>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "GetStringAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.String") &&
            TryGetRequestUriShape(symbol, allowCompletionOption: false, out parameters))
        {
            target = HttpTarget(symbol, "GetStringAsync", "global::System.Threading.Tasks.Task<string>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "GetByteArrayAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Byte[]") &&
            TryGetRequestUriShape(symbol, allowCompletionOption: false, out parameters))
        {
            target = HttpTarget(symbol, "GetByteArrayAsync", "global::System.Threading.Tasks.Task<byte[]>", parameters);
            return true;
        }

        if (string.Equals(symbol.Name, "GetStreamAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.IO.Stream") &&
            TryGetRequestUriShape(symbol, allowCompletionOption: false, out parameters))
        {
            target = HttpTarget(symbol, "GetStreamAsync", "global::System.Threading.Tasks.Task<global::System.IO.Stream>", parameters);
            return true;
        }

        return false;
    }

    private static bool TryGetDbCommandInvocation(
        IMethodSymbol symbol,
        ITypeSymbol? receiverType,
        out InterceptorTarget target)
    {
        target = default;
        if (!InheritsFromOrIs(symbol.ContainingType, "global::System.Data.Common.DbCommand"))
            return false;

        var effectiveReceiverType = receiverType is not null &&
                                    InheritsFromOrIs(receiverType, "global::System.Data.Common.DbCommand")
            ? receiverType
            : symbol.ContainingType;

        var methodName = symbol.Name;
        var isAsync = methodName.EndsWithOrdinal("Async");
        if (!TryGetDbCommandParameters(symbol, methodName, out var parameters))
            return false;

        if (!TryGetDbCommandReturn(symbol, methodName, isAsync, out var returnType))
            return false;

        var instrumentationId = GetDbInstrumentationId(effectiveReceiverType);
        target = new InterceptorTarget(
            InterceptorKind.DbCommand,
            TelemetrySignal.Traces,
            instrumentationId,
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            isAsync,
            AdditionalMetricIds: GetDbMetricIds(instrumentationId));
        return true;
    }

    private static bool TryGetElasticInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        if (TryGetElasticsearchClientInvocation(symbol, out target))
            return true;

        return TryGetElasticTransportInvocation(symbol, out target);
    }

    private static bool TryGetElasticsearchClientInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
            symbol.ReturnsVoid ||
            symbol.DeclaredAccessibility is not Accessibility.Public ||
            !CanEmitByValueOrInParameters(symbol) ||
            !IsElasticsearchClientType(symbol.ContainingType) ||
            !CanEmitElasticReturn(symbol.ReturnType, out var isAsync))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.ElasticsearchClient,
            TelemetrySignal.Traces,
            "ELASTICSEARCH",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            isAsync,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol));
        return true;
    }

    private static bool TryGetElasticTransportInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        if (!IsOrImplementsType(receiverType, "Elastic.Transport", "ITransport") &&
            (!TryGetReducedExtensionReceiverType(symbol, out receiverType) ||
             !IsOrImplementsType(receiverType, "Elastic.Transport", "ITransport")))
        {
            return false;
        }

        if (!IsSupportedElasticTransportMethod(symbol.Name) ||
            symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary and not MethodKind.ReducedExtension ||
            symbol.ReturnsVoid ||
            !CanEmitByValueOrInParameters(symbol) ||
            !CanEmitElasticReturn(symbol.ReturnType, out var isAsync))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.ElasticTransport,
            TelemetrySignal.Traces,
            "ELASTICTRANSPORT",
            CleanTypeName(receiverType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            isAsync,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol),
            GetReducedExtensionContainingType(symbol));
        return true;
    }

    private static bool IsElasticsearchClientType(ITypeSymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol named ||
            !named.Name.EndsWithOrdinal("Client"))
        {
            return false;
        }

        return named.ContainingNamespace.ToDisplayString().StartsWithOrdinal("Elastic.Clients.Elasticsearch");
    }

    private static bool IsSupportedElasticTransportMethod(string methodName)
        => methodName is "Request" or "RequestAsync";

    private static bool CanEmitElasticReturn(ITypeSymbol returnType, out bool isAsync)
    {
        isAsync = false;
        if (IsTask(returnType))
        {
            isAsync = true;
            return true;
        }

        if (TryGetTaskResult(returnType, out _))
        {
            isAsync = true;
            return true;
        }

        return true;
    }

    private static bool TryGetWcfClientInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
            symbol.IsGenericMethod ||
            IsWcfInfrastructureMethod(symbol.Name) ||
            !CanEmitByValueOrInParameters(symbol) ||
            !InheritsFromConstructedGeneric(symbol.ContainingType, "global::System.ServiceModel.ClientBase<TChannel>") ||
            IsSystemServiceModelType(symbol.ContainingType))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.WcfClient,
            TelemetrySignal.Traces,
            "WCFCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            IsTask(symbol.ReturnType) || TryGetTaskResult(symbol.ReturnType, out _));
        return true;
    }

    private static bool IsWcfInfrastructureMethod(string methodName)
        => methodName is "Open" or
            "OpenAsync" or
            "Close" or
            "CloseAsync" or
            "Abort" or
            "Dispose" or
            "GetProperty" or
            "BeginOpen" or
            "EndOpen" or
            "BeginClose" or
            "EndClose";

    private static bool IsSystemServiceModelType(ITypeSymbol? symbol)
        => symbol is INamedTypeSymbol named &&
           named.ContainingNamespace.ToDisplayString().StartsWithOrdinal("System.ServiceModel");

    private static bool TryGetGrpcNetClientAsyncUnaryInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!IsConstructedFrom(symbol.ReturnType, "global::Grpc.Core.AsyncUnaryCall<TResponse>") ||
            !InheritsFromConstructedGeneric(symbol.ContainingType, "global::Grpc.Core.ClientBase<T>"))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.GrpcNetClientAsyncUnaryCall,
            TelemetrySignal.Traces,
            "GRPCNETCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            false);
        return true;
    }

    private static bool TryGetGrpcNetClientStreamingInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!InheritsFromConstructedGeneric(symbol.ContainingType, "global::Grpc.Core.ClientBase<T>"))
            return false;

        var kind = default(InterceptorKind);
        if (IsConstructedFrom(symbol.ReturnType, "global::Grpc.Core.AsyncServerStreamingCall<TResponse>"))
        {
            kind = InterceptorKind.GrpcNetClientAsyncServerStreamingCall;
        }
        else if (IsConstructedFrom(symbol.ReturnType, "global::Grpc.Core.AsyncClientStreamingCall<TRequest, TResponse>"))
        {
            kind = InterceptorKind.GrpcNetClientAsyncClientStreamingCall;
        }
        else if (IsConstructedFrom(symbol.ReturnType, "global::Grpc.Core.AsyncDuplexStreamingCall<TRequest, TResponse>"))
        {
            kind = InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall;
        }
        else
        {
            return false;
        }

        target = new InterceptorTarget(
            kind,
            TelemetrySignal.Traces,
            "GRPCNETCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            false);
        return true;
    }

    private static bool TryGetKafkaInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (TryGetKafkaProducerInvocation(symbol, out target))
            return true;

        return TryGetKafkaConsumerInvocation(symbol, out target);
    }

    private static bool TryGetMassTransitInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        if (!IsMassTransitEndpointType(receiverType) &&
            (!TryGetReducedExtensionReceiverType(symbol, out receiverType) ||
             !IsMassTransitEndpointType(receiverType)))
        {
            return false;
        }

        if (!IsSupportedMassTransitOperation(symbol.Name) ||
            !IsTask(symbol.ReturnType) ||
            symbol.Parameters.Length is 0)
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.MassTransitMessageOperation,
            TelemetrySignal.Traces,
            "MASSTRANSIT",
            CleanTypeName(receiverType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            true,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol),
            GetReducedExtensionContainingType(symbol));
        return true;
    }

    private static bool IsSupportedMassTransitOperation(string methodName)
        => methodName is "Publish" or "Send";

    private static bool IsMassTransitEndpointType(ITypeSymbol? symbol)
        => IsOrImplementsType(symbol, "MassTransit", "IPublishEndpoint") ||
           IsOrImplementsType(symbol, "MassTransit", "ISendEndpoint") ||
           IsOrImplementsType(symbol, "MassTransit", "ISendEndpointProvider");

    private static bool TryGetNServiceBusInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        if (!IsNServiceBusEndpointType(receiverType) &&
            (!TryGetReducedExtensionReceiverType(symbol, out receiverType) ||
             !IsNServiceBusEndpointType(receiverType)))
        {
            return false;
        }

        if (!IsSupportedNServiceBusOperation(symbol.Name) ||
            !IsTask(symbol.ReturnType) ||
            symbol.Parameters.Length is 0)
        {
            return false;
        }

        var typeParameterList = GetTypeParameterList(symbol);
        var receiverTypeName = CleanTypeName(receiverType);
        var returnTypeName = CleanTypeName(symbol.ReturnType, symbol);
        var parameters = BuildParameters(symbol);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromVisibleTypes(symbol, receiverType);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromFormattedTypes(receiverTypeName, returnTypeName, parameters);

        target = new InterceptorTarget(
            InterceptorKind.NServiceBusMessageOperation,
            TelemetrySignal.Traces,
            "NSERVICEBUS",
            receiverTypeName,
            symbol.Name,
            returnTypeName,
            parameters,
            true,
            typeParameterList,
            GetConstraintClauses(symbol),
            GetReducedExtensionContainingType(symbol),
            AdditionalMetricIds: MetricIds("NSERVICEBUS"));
        return true;
    }

    private static bool IsSupportedNServiceBusOperation(string methodName)
        => methodName is "Publish" or "Send";

    private static bool IsNServiceBusEndpointType(ITypeSymbol? symbol)
        => IsOrImplementsType(symbol, "NServiceBus", "IMessageSession") ||
           IsOrImplementsType(symbol, "NServiceBus", "IMessageHandlerContext");

    private static bool TryGetQuartzInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!string.Equals(symbol.Name, "Execute", StringComparison.Ordinal) ||
            !IsTask(symbol.ReturnType) ||
            !IsOrImplementsType(symbol.ContainingType, "Quartz", "IJob") ||
            symbol.Parameters.Length is not 1 ||
            !IsType(symbol.Parameters[0].Type, "global::Quartz.IJobExecutionContext"))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.QuartzJobExecute,
            TelemetrySignal.Traces,
            "QUARTZ",
            CleanTypeName(symbol.ContainingType),
            "Execute",
            "global::System.Threading.Tasks.Task",
            BuildParameters(symbol),
            true);
        return true;
    }

    private static bool TryGetStackExchangeRedisInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!IsSupportedRedisAsyncCommand(symbol.Name) ||
            !IsOrImplementsType(symbol.ContainingType, "StackExchange.Redis", "IDatabaseAsync") ||
            !TryGetTaskResult(symbol.ReturnType, out _) ||
            !TryGetRedisCommandParameters(symbol, out var parameters))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.StackExchangeRedisCommandAsync,
            TelemetrySignal.Traces,
            "STACKEXCHANGEREDIS",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            parameters,
            true);
        return true;
    }

    private static bool IsSupportedRedisAsyncCommand(string methodName)
        => methodName is "StringGetAsync" or
            "StringSetAsync" or
            "StringIncrementAsync" or
            "StringDecrementAsync" or
            "HashGetAsync" or
            "HashSetAsync" or
            "HashDeleteAsync" or
            "HashExistsAsync" or
            "KeyDeleteAsync" or
            "KeyExistsAsync" or
            "ListLeftPushAsync" or
            "ListRightPushAsync" or
            "SetAddAsync" or
            "SetRemoveAsync" or
            "SortedSetAddAsync" or
            "SortedSetRemoveAsync" or
            "ExecuteAsync";

    private static string GetRedisOperationName(string methodName)
        => methodName switch
        {
            "StringGetAsync" => "GET",
            "StringSetAsync" => "SET",
            "StringIncrementAsync" => "INCR",
            "StringDecrementAsync" => "DECR",
            "HashGetAsync" => "HGET",
            "HashSetAsync" => "HSET",
            "HashDeleteAsync" => "HDEL",
            "HashExistsAsync" => "HEXISTS",
            "KeyDeleteAsync" => "DEL",
            "KeyExistsAsync" => "EXISTS",
            "ListLeftPushAsync" => "LPUSH",
            "ListRightPushAsync" => "RPUSH",
            "SetAddAsync" => "SADD",
            "SetRemoveAsync" => "SREM",
            "SortedSetAddAsync" => "ZADD",
            "SortedSetRemoveAsync" => "ZREM",
            _ => "EXECUTE",
        };

    private static bool TryGetGraphQlInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!string.Equals(symbol.Name, "ExecuteAsync", StringComparison.Ordinal) ||
            !IsOrImplementsType(symbol.ContainingType, "GraphQL", "IDocumentExecuter") ||
            !TryGetTaskResult(symbol.ReturnType, out var resultType) ||
            resultType is not INamedTypeSymbol namedResult ||
            !IsTypeByMetadata(namedResult, "GraphQL", "ExecutionResult"))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.GraphQlDocumentExecuter,
            TelemetrySignal.Traces,
            "GRAPHQL",
            CleanTypeName(symbol.ContainingType),
            "ExecuteAsync",
            CleanTypeName(symbol.ReturnType, symbol),
            BuildParameters(symbol),
            true);
        return true;
    }

    private static bool TryGetMongoDbInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        if (!IsOrImplementsConstructedGeneric(receiverType, "MongoDB.Driver", "IMongoCollection`1") &&
            (!TryGetReducedExtensionReceiverType(symbol, out receiverType) ||
             !IsOrImplementsConstructedGeneric(receiverType, "MongoDB.Driver", "IMongoCollection`1")))
        {
            return false;
        }

        if (!IsSupportedMongoDbCollectionMethod(symbol.Name) ||
            !CanEmitMongoDbReturn(symbol.ReturnType))
        {
            return false;
        }

        var typeParameterList = GetTypeParameterList(symbol);
        var receiverTypeName = CleanTypeName(receiverType);
        var returnTypeName = CleanTypeName(symbol.ReturnType, symbol);
        var parameters = BuildParameters(symbol);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromVisibleTypes(symbol, receiverType);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromFormattedTypes(receiverTypeName, returnTypeName, parameters);

        target = new InterceptorTarget(
            InterceptorKind.MongoDbCollection,
            TelemetrySignal.Traces,
            "MONGODB",
            receiverTypeName,
            symbol.Name,
            returnTypeName,
            parameters,
            IsTask(symbol.ReturnType) || TryGetTaskResult(symbol.ReturnType, out _),
            typeParameterList,
            string.Empty,
            GetReducedExtensionContainingType(symbol));
        return true;
    }

    private static bool IsSupportedMongoDbCollectionMethod(string methodName)
        => methodName is "Find" or
            "FindAsync" or
            "Aggregate" or
            "AggregateAsync" or
            "InsertOne" or
            "InsertOneAsync" or
            "InsertMany" or
            "InsertManyAsync" or
            "ReplaceOne" or
            "ReplaceOneAsync" or
            "DeleteOne" or
            "DeleteOneAsync" or
            "DeleteMany" or
            "DeleteManyAsync" or
            "UpdateOne" or
            "UpdateOneAsync" or
            "UpdateMany" or
            "UpdateManyAsync" or
            "CountDocuments" or
            "CountDocumentsAsync" or
            "EstimatedDocumentCount" or
            "EstimatedDocumentCountAsync";

    private static bool CanEmitMongoDbReturn(ITypeSymbol returnType)
        => returnType.SpecialType is SpecialType.System_Void ||
           IsTask(returnType) ||
           TryGetTaskResult(returnType, out _) ||
           returnType.SpecialType is not SpecialType.None ||
           returnType is INamedTypeSymbol;

    private static bool TryGetRabbitMqInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        if (!IsRabbitMqChannelType(receiverType) &&
            (!TryGetReducedExtensionReceiverType(symbol, out receiverType) ||
             !IsRabbitMqChannelType(receiverType)))
        {
            return false;
        }

        if (!TryGetRabbitMqBasicPublishParameters(symbol, out var parameters))
        {
            return false;
        }

        if (string.Equals(symbol.Name, "BasicPublish", StringComparison.Ordinal) &&
            symbol.ReturnsVoid)
        {
            target = new InterceptorTarget(
                InterceptorKind.RabbitMqBasicPublish,
                TelemetrySignal.Traces,
                "RABBITMQ",
                CleanTypeName(receiverType),
                "BasicPublish",
                "void",
                parameters,
                false,
                ExtensionContainingType: GetReducedExtensionContainingType(symbol));
            return true;
        }

        if (string.Equals(symbol.Name, "BasicPublishAsync", StringComparison.Ordinal) &&
            IsValueTask(symbol.ReturnType))
        {
            target = new InterceptorTarget(
                InterceptorKind.RabbitMqBasicPublish,
                TelemetrySignal.Traces,
                "RABBITMQ",
                CleanTypeName(receiverType),
                "BasicPublishAsync",
                CleanTypeName(symbol.ReturnType, symbol),
                parameters,
                true,
                GetTypeParameterList(symbol),
                GetConstraintClauses(symbol),
                GetReducedExtensionContainingType(symbol));
            return true;
        }

        return false;
    }

    private static bool IsRabbitMqChannelType(ITypeSymbol? symbol)
        => IsOrImplementsType(symbol, "RabbitMQ.Client", "IModel") ||
           IsOrImplementsType(symbol, "RabbitMQ.Client", "IChannel");

    private static bool TryGetLoggerInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!string.Equals(symbol.Name, "Log", StringComparison.Ordinal) ||
            !symbol.ReturnsVoid ||
            !symbol.IsGenericMethod ||
            symbol.TypeParameters.Length is not 1 ||
            !IsType(symbol.ContainingType, "global::Microsoft.Extensions.Logging.ILogger") ||
            symbol.Parameters.Length is not 5 ||
            !IsType(symbol.Parameters[0].Type, "global::Microsoft.Extensions.Logging.LogLevel") ||
            !IsType(symbol.Parameters[1].Type, "global::Microsoft.Extensions.Logging.EventId") ||
            !IsLoggerFormatter(symbol.Parameters[4].Type))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.ILoggerLog,
            TelemetrySignal.Logs,
            "ILOGGER",
            CleanTypeName(symbol.ContainingType),
            "Log",
            "void",
            BuildParameters(symbol),
            false);
        return true;
    }

    private static bool TryGetLoggerExtensionInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        var original = symbol.ReducedFrom;
        if (original is null ||
            !symbol.ReturnsVoid ||
            !IsType(original.ContainingType, "global::Microsoft.Extensions.Logging.LoggerExtensions") ||
            !IsSupportedLoggerExtensionName(symbol.Name) ||
            !IsSupportedLoggerExtensionParameters(symbol))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.ILoggerExtensionLog,
            TelemetrySignal.Logs,
            "ILOGGER",
            "global::Microsoft.Extensions.Logging.ILogger",
            symbol.Name,
            "void",
            BuildParameters(symbol),
            false);
        return true;
    }

    private static bool IsSupportedLoggerExtensionName(string name)
        => string.Equals(name, "Log", StringComparison.Ordinal) || GetLoggerExtensionLevelName(name) is not null;

    private static string? GetLoggerExtensionLevelName(string methodName)
        => methodName switch
        {
            "LogTrace" => "Trace",
            "LogDebug" => "Debug",
            "LogInformation" => "Information",
            "LogWarning" => "Warning",
            "LogError" => "Error",
            "LogCritical" => "Critical",
            _ => null,
        };

    private static bool IsSupportedLoggerExtensionParameters(IMethodSymbol symbol)
    {
        if (symbol.Parameters.Length is < 2)
            return false;

        var hasMessage = false;
        var hasArgs = false;

        foreach (var parameter in symbol.Parameters)
        {
            if (IsType(parameter.Type, "global::Microsoft.Extensions.Logging.LogLevel") ||
                IsType(parameter.Type, "global::Microsoft.Extensions.Logging.EventId") ||
                IsType(parameter.Type, "global::System.Exception"))
            {
                continue;
            }

            if (IsType(parameter.Type, "global::System.String"))
            {
                hasMessage = true;
                continue;
            }

            if (parameter.IsParams && IsArrayOf(parameter.Type, "global::System.Object"))
            {
                hasArgs = true;
                continue;
            }

            return false;
        }

        return hasMessage && hasArgs;
    }

    private static bool TryGetNLogInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!symbol.ReturnsVoid ||
            !IsTypeByMetadata(symbol.ContainingType, "NLog", "Logger") ||
            !IsSupportedExternalLoggerMethodName(symbol.Name))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.NLogLogger,
            TelemetrySignal.Logs,
            "NLOG",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            "void",
            BuildParameters(symbol),
            false,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol),
            ExtensionContainingType: GetExternalLoggerEnabledProperty(symbol));
        return true;
    }

    private static bool TryGetLog4NetInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!symbol.ReturnsVoid ||
            !IsLog4NetLoggerType(symbol.ContainingType) ||
            !IsSupportedExternalLoggerMethodName(symbol.Name))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.Log4NetLogger,
            TelemetrySignal.Logs,
            "LOG4NET",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            "void",
            BuildParameters(symbol),
            false,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol),
            ExtensionContainingType: GetExternalLoggerEnabledProperty(symbol));
        return true;
    }

    private static bool IsSupportedExternalLoggerMethodName(string name)
        => name is "Log" or
            "Trace" or "TraceFormat" or
            "Debug" or "DebugFormat" or
            "Info" or "InfoFormat" or
            "Warn" or "WarnFormat" or
            "Warning" or "WarningFormat" or
            "Error" or "ErrorFormat" or
            "Fatal" or "FatalFormat" or
            "Critical" or "CriticalFormat";

    private static string GetExternalLoggerEnabledProperty(IMethodSymbol symbol)
    {
        var propertyName = symbol.Name switch
        {
            "Trace" or "TraceFormat" => "IsTraceEnabled",
            "Debug" or "DebugFormat" => "IsDebugEnabled",
            "Info" or "InfoFormat" => "IsInfoEnabled",
            "Warn" or "WarnFormat" or "Warning" or "WarningFormat" => "IsWarnEnabled",
            "Error" or "ErrorFormat" => "IsErrorEnabled",
            "Fatal" or "FatalFormat" or "Critical" or "CriticalFormat" => "IsFatalEnabled",
            _ => string.Empty,
        };

        return propertyName.Length > 0 && HasReadableBooleanProperty(symbol.ContainingType, propertyName)
            ? propertyName
            : string.Empty;
    }

    private static bool HasReadableBooleanProperty(ITypeSymbol? symbol, string propertyName)
    {
        for (var current = symbol; current is not null; current = current.BaseType)
        {
            foreach (var member in current.GetMembers(propertyName))
            {
                if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_Boolean, GetMethod: not null })
                    return true;
            }
        }

        if (symbol is INamedTypeSymbol named)
        {
            foreach (var interfaceType in named.AllInterfaces)
            {
                foreach (var member in interfaceType.GetMembers(propertyName))
                {
                    if (member is IPropertySymbol { Type.SpecialType: SpecialType.System_Boolean, GetMethod: not null })
                        return true;
                }
            }
        }

        return false;
    }

    private static bool IsLog4NetLoggerType(ITypeSymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol named)
            return false;

        if (IsTypeByMetadata(named, "log4net", "ILog") ||
            IsTypeByMetadata(named, "log4net.Core", "ILogger"))
            return true;

        foreach (var interfaceType in named.AllInterfaces)
        {
            if (IsTypeByMetadata(interfaceType, "log4net", "ILog") ||
                IsTypeByMetadata(interfaceType, "log4net.Core", "ILogger"))
                return true;
        }

        return false;
    }

    private static bool TryGetKafkaProducerInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!IsOrImplementsConstructedGeneric(symbol.ContainingType, "Confluent.Kafka", "IProducer`2"))
        {
            return false;
        }

        if (string.Equals(symbol.Name, "ProduceAsync", StringComparison.Ordinal) &&
            TryGetTaskResult(symbol.ReturnType, out var resultType) &&
            IsConstructedGeneric(resultType, "Confluent.Kafka", "DeliveryResult`2") &&
            TryGetKafkaProduceParameters(symbol, isAsync: true, out var parameters))
        {
            target = new InterceptorTarget(
                InterceptorKind.KafkaProducer,
                TelemetrySignal.Traces,
                "KAFKA",
                CleanTypeName(symbol.ContainingType),
                "ProduceAsync",
                CleanTypeName(symbol.ReturnType, symbol),
                parameters,
                true);
            return true;
        }

        if (string.Equals(symbol.Name, "Produce", StringComparison.Ordinal) &&
            symbol.ReturnsVoid &&
            TryGetKafkaProduceParameters(symbol, isAsync: false, out parameters))
        {
            target = new InterceptorTarget(
                InterceptorKind.KafkaProducer,
                TelemetrySignal.Traces,
                "KAFKA",
                CleanTypeName(symbol.ContainingType),
                "Produce",
                "void",
                parameters,
                false);
            return true;
        }

        return false;
    }

    private static bool TryGetKafkaConsumerInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!string.Equals(symbol.Name, "Consume", StringComparison.Ordinal) ||
            !IsOrImplementsConstructedGeneric(symbol.ContainingType, "Confluent.Kafka", "IConsumer`2") ||
            !IsConstructedGeneric(symbol.ReturnType, "Confluent.Kafka", "ConsumeResult`2") ||
            !TryGetKafkaConsumeParameters(symbol, out var parameters))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.KafkaConsumer,
            TelemetrySignal.Traces,
            "KAFKA",
            CleanTypeName(symbol.ContainingType),
            "Consume",
            CleanTypeName(symbol.ReturnType, symbol),
            parameters,
            false);
        return true;
    }

}
