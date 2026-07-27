using ANcpLua.Roslyn.Utilities;
using System.Collections.Immutable;
using Microsoft.CodeAnalysis;

namespace Qyl.Telemetry.AutoInstrumentation.SourceGenerators;

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
        var contractType = GetWcfContractType(symbol.ContainingType);
        if (symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
            symbol.IsGenericMethod ||
            IsWcfInfrastructureMethod(symbol.Name) ||
            !CanEmitByValueOrInParameters(symbol) ||
            contractType is null ||
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
            IsTask(symbol.ReturnType) || TryGetTaskResult(symbol.ReturnType, out _),
            SemanticName: GetWcfMethodName(contractType, symbol));
        return true;
    }

    private static INamedTypeSymbol? GetWcfContractType(INamedTypeSymbol clientType)
    {
        for (var current = clientType; current is not null; current = current.BaseType)
        {
            if (IsType(current.ConstructedFrom, "global::System.ServiceModel.ClientBase<TChannel>") &&
                current.TypeArguments is [INamedTypeSymbol contractType])
            {
                return contractType;
            }
        }

        return null;
    }

    private static string GetWcfMethodName(INamedTypeSymbol contractType, IMethodSymbol implementation)
    {
        var serviceName = GetAttributeName(
                              contractType,
                              "System.ServiceModel.ServiceContractAttribute") ??
                          contractType.Name;
        var operationName = implementation.Name;
        foreach (var member in contractType.GetMembers(implementation.Name))
        {
            if (member is IMethodSymbol contractMethod &&
                contractMethod.Parameters.Length == implementation.Parameters.Length)
            {
                operationName = GetAttributeName(
                                    contractMethod,
                                    "System.ServiceModel.OperationContractAttribute") ??
                                contractMethod.Name;
                break;
            }
        }

        return serviceName + "/" + operationName;
    }

    private static string? GetAttributeName(ISymbol symbol, string attributeType)
    {
        foreach (var attribute in symbol.GetAttributes())
        {
            if (!string.Equals(attribute.AttributeClass?.ToDisplayString(), attributeType, StringComparison.Ordinal))
                continue;

            foreach (var argument in attribute.NamedArguments)
            {
                if (string.Equals(argument.Key, "Name", StringComparison.Ordinal) &&
                    argument.Value.Value is string { Length: > 0 } name)
                {
                    return name;
                }
            }
        }

        return null;
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
        if (!IsOrImplementsType(symbol.ContainingType, "StackExchange.Redis", "IDatabaseAsync") ||
            !(IsTask(symbol.ReturnType) || TryGetTaskResult(symbol.ReturnType, out _)) ||
            !TryGetRedisCommandParameters(symbol, out var parameters) ||
            !TryGetRedisOperation(symbol.Name, parameters, out _))
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

    private const string RedisKeyArrayType = "global::StackExchange.Redis.RedisKey[]";
    private const string RedisValueArrayType = "global::StackExchange.Redis.RedisValue[]";
    private const string RedisHashEntryArrayType = "global::StackExchange.Redis.HashEntry[]";
    private const string RedisWhenType = "global::StackExchange.Redis.When";
    private const string RedisOrderType = "global::StackExchange.Redis.Order";
    private const string RedisExpirationType = "global::StackExchange.Redis.Expiration";

    /// <summary>
    /// Resolves the Redis command an <c>IDatabaseAsync</c> overload puts on the wire. Support and
    /// naming are the same decision, so an unmapped overload is not instrumented rather than
    /// labelled with a guessed command. Overloads that only differ on the wire by an argument
    /// <em>value</em> carry a discriminator the interceptor evaluates at the call site.
    /// Every mapping is captured from a live server by <c>demos/Qyl.RealRedisDemo</c>, which
    /// compares this name against <c>IProfiledCommand.Command</c>.
    /// </summary>
    private static bool TryGetRedisOperation(
        string methodName,
        EquatableArray<ParameterSpec> parameters,
        out RedisOperationSpec operation)
    {
        operation = default;

        switch (methodName)
        {
            case "ExecuteAsync":
                operation = new RedisOperationSpec(string.Empty, CommandTextParameter: parameters[0].Name);
                return true;

            case "StringGetAsync":
                operation = new RedisOperationSpec(IsRedisParameter(parameters, 0, RedisKeyArrayType) ? "MGET" : "GET");
                return true;
            case "StringSetAsync":
                // ValueCondition.NotExists reaches SETNX, but ValueCondition carries neither an
                // equality operator nor IEquatable, so testing for it at the call site would box
                // on every intercepted SET, including the calls that have tracing switched off.
                // SET is reported for the whole method rather than charging every caller for the
                // one branch that differs.
                operation = new RedisOperationSpec("SET");
                return true;
            case "StringIncrementAsync":
                return TryGetRedisIncrementOperation(parameters, "INCR", "INCRBY", out operation);
            case "StringDecrementAsync":
                return TryGetRedisIncrementOperation(parameters, "DECR", "DECRBY", out operation);
            case "StringAppendAsync":
                operation = new RedisOperationSpec("APPEND");
                return true;
            case "StringLengthAsync":
                operation = new RedisOperationSpec("STRLEN");
                return true;
            case "StringGetRangeAsync":
                operation = new RedisOperationSpec("GETRANGE");
                return true;
            case "StringSetRangeAsync":
                operation = new RedisOperationSpec("SETRANGE");
                return true;
            case "StringGetSetAsync":
                operation = new RedisOperationSpec("GETSET");
                return true;
            case "StringGetDeleteAsync":
                operation = new RedisOperationSpec("GETDEL");
                return true;
            case "StringGetSetExpiryAsync":
                operation = new RedisOperationSpec("GETEX");
                return true;
            case "StringGetBitAsync":
                operation = new RedisOperationSpec("GETBIT");
                return true;
            case "StringSetBitAsync":
                operation = new RedisOperationSpec("SETBIT");
                return true;

            case "HashGetAsync":
                operation = new RedisOperationSpec(IsRedisParameter(parameters, 1, RedisValueArrayType) ? "HMGET" : "HGET");
                return true;
            case "HashSetAsync":
                operation = IsRedisParameter(parameters, 1, RedisHashEntryArrayType)
                    ? new RedisOperationSpec("HMSET")
                    : RedisWhenOperation(parameters, "HSET", "HSETNX", "NotExists");
                return true;
            case "HashDeleteAsync":
                operation = new RedisOperationSpec("HDEL");
                return true;
            case "HashExistsAsync":
                operation = new RedisOperationSpec("HEXISTS");
                return true;
            case "HashGetAllAsync":
                operation = new RedisOperationSpec("HGETALL");
                return true;
            case "HashKeysAsync":
                operation = new RedisOperationSpec("HKEYS");
                return true;
            case "HashValuesAsync":
                operation = new RedisOperationSpec("HVALS");
                return true;
            case "HashLengthAsync":
                operation = new RedisOperationSpec("HLEN");
                return true;
            case "HashStringLengthAsync":
                operation = new RedisOperationSpec("HSTRLEN");
                return true;
            case "HashRandomFieldAsync":
                operation = new RedisOperationSpec("HRANDFIELD");
                return true;
            case "HashIncrementAsync":
            case "HashDecrementAsync":
                operation = new RedisOperationSpec(IsRedisParameter(parameters, 2, "double") ? "HINCRBYFLOAT" : "HINCRBY");
                return true;

            case "KeyDeleteAsync":
                operation = new RedisOperationSpec("DEL");
                return true;
            case "KeyExistsAsync":
                operation = new RedisOperationSpec("EXISTS");
                return true;
            case "KeyExpireAsync":
                return TryGetRedisExpireOperation(parameters, out operation);
            case "KeyTimeToLiveAsync":
                operation = new RedisOperationSpec("PTTL");
                return true;
            case "KeyPersistAsync":
                operation = new RedisOperationSpec("PERSIST");
                return true;
            case "KeyTypeAsync":
                operation = new RedisOperationSpec("TYPE");
                return true;
            case "KeyRenameAsync":
                operation = RedisWhenOperation(parameters, "RENAME", "RENAMENX", "NotExists");
                return true;
            case "KeyTouchAsync":
                operation = new RedisOperationSpec("TOUCH");
                return true;
            case "KeyDumpAsync":
                operation = new RedisOperationSpec("DUMP");
                return true;
            case "KeyCopyAsync":
                operation = new RedisOperationSpec("COPY");
                return true;
            case "KeyMoveAsync":
                operation = new RedisOperationSpec("MOVE");
                return true;
            case "KeyIdleTimeAsync":
                operation = new RedisOperationSpec("OBJECT");
                return true;

            case "ListLeftPushAsync":
                operation = RedisWhenOperation(parameters, "LPUSH", "LPUSHX", "Exists");
                return true;
            case "ListRightPushAsync":
                operation = RedisWhenOperation(parameters, "RPUSH", "RPUSHX", "Exists");
                return true;
            case "ListLeftPopAsync":
                return TryGetRedisSingleKeyOperation(parameters, "LPOP", out operation);
            case "ListRightPopAsync":
                return TryGetRedisSingleKeyOperation(parameters, "RPOP", out operation);
            case "ListLengthAsync":
                operation = new RedisOperationSpec("LLEN");
                return true;
            case "ListRangeAsync":
                operation = new RedisOperationSpec("LRANGE");
                return true;
            case "ListGetByIndexAsync":
                operation = new RedisOperationSpec("LINDEX");
                return true;
            case "ListRemoveAsync":
                operation = new RedisOperationSpec("LREM");
                return true;
            case "ListSetByIndexAsync":
                operation = new RedisOperationSpec("LSET");
                return true;
            case "ListTrimAsync":
                operation = new RedisOperationSpec("LTRIM");
                return true;
            case "ListInsertBeforeAsync":
            case "ListInsertAfterAsync":
                operation = new RedisOperationSpec("LINSERT");
                return true;
            case "ListPositionAsync":
                operation = new RedisOperationSpec("LPOS");
                return true;
            case "ListMoveAsync":
                operation = new RedisOperationSpec("LMOVE");
                return true;
            case "ListRightPopLeftPushAsync":
                operation = new RedisOperationSpec("RPOPLPUSH");
                return true;

            case "SetAddAsync":
                operation = new RedisOperationSpec("SADD");
                return true;
            case "SetRemoveAsync":
                operation = new RedisOperationSpec("SREM");
                return true;
            case "SetContainsAsync":
                operation = new RedisOperationSpec(IsRedisParameter(parameters, 1, RedisValueArrayType) ? "SMISMEMBER" : "SISMEMBER");
                return true;
            case "SetMembersAsync":
                operation = new RedisOperationSpec("SMEMBERS");
                return true;
            case "SetLengthAsync":
                operation = new RedisOperationSpec("SCARD");
                return true;
            case "SetPopAsync":
                operation = new RedisOperationSpec("SPOP");
                return true;
            case "SetRandomMemberAsync":
            case "SetRandomMembersAsync":
                operation = new RedisOperationSpec("SRANDMEMBER");
                return true;
            case "SetMoveAsync":
                operation = new RedisOperationSpec("SMOVE");
                return true;

            case "SortedSetAddAsync":
                operation = new RedisOperationSpec("ZADD");
                return true;
            case "SortedSetRemoveAsync":
                operation = new RedisOperationSpec("ZREM");
                return true;
            case "SortedSetIncrementAsync":
            case "SortedSetDecrementAsync":
                operation = new RedisOperationSpec("ZINCRBY");
                return true;
            case "SortedSetLengthAsync":
                operation = new RedisOperationSpec("ZCARD");
                return true;
            case "SortedSetLengthByValueAsync":
                operation = new RedisOperationSpec("ZLEXCOUNT");
                return true;
            case "SortedSetScoreAsync":
                operation = new RedisOperationSpec("ZSCORE");
                return true;
            case "SortedSetScoresAsync":
                operation = new RedisOperationSpec("ZMSCORE");
                return true;
            case "SortedSetRankAsync":
                operation = RedisOrderOperation(parameters, "ZRANK", "ZREVRANK");
                return true;
            case "SortedSetRangeByRankAsync":
            case "SortedSetRangeByRankWithScoresAsync":
                operation = RedisOrderOperation(parameters, "ZRANGE", "ZREVRANGE");
                return true;
            case "SortedSetRangeByScoreAsync":
            case "SortedSetRangeByScoreWithScoresAsync":
                operation = RedisOrderOperation(parameters, "ZRANGEBYSCORE", "ZREVRANGEBYSCORE");
                return true;
            case "SortedSetRangeByValueAsync":
                operation = RedisOrderOperation(parameters, "ZRANGEBYLEX", "ZREVRANGEBYLEX");
                return true;
            case "SortedSetRemoveRangeByRankAsync":
                operation = new RedisOperationSpec("ZREMRANGEBYRANK");
                return true;
            case "SortedSetRemoveRangeByScoreAsync":
                operation = new RedisOperationSpec("ZREMRANGEBYSCORE");
                return true;
            case "SortedSetRemoveRangeByValueAsync":
                operation = new RedisOperationSpec("ZREMRANGEBYLEX");
                return true;
            case "SortedSetPopAsync":
                if (IsRedisParameter(parameters, 0, RedisKeyArrayType))
                    return false;

                operation = RedisOrderOperation(parameters, "ZPOPMIN", "ZPOPMAX");
                return true;
            case "SortedSetRandomMemberAsync":
                operation = new RedisOperationSpec("ZRANDMEMBER");
                return true;

            default:
                return false;
        }
    }

    /// <summary>
    /// StackExchange.Redis collapses an increment of exactly one onto the unit command and
    /// routes the floating-point overload through <c>INCRBYFLOAT</c> in both directions.
    /// </summary>
    private static bool TryGetRedisIncrementOperation(
        EquatableArray<ParameterSpec> parameters,
        string unitCommand,
        string byCommand,
        out RedisOperationSpec operation)
    {
        operation = default;

        // The bounded-increment overloads are marked experimental upstream and reach a wire
        // command this table has not pinned against a live server.
        if (IndexOfRedisParameter(parameters, RedisExpirationType) >= 0)
            return false;

        if (IsRedisParameter(parameters, 1, "double"))
        {
            operation = new RedisOperationSpec("INCRBYFLOAT");
            return true;
        }

        if (!IsRedisParameter(parameters, 1, "long"))
            return false;

        operation = new RedisOperationSpec(
            byCommand,
            RedisBranches(new RedisCommandBranch(parameters[1].Name + " == 1L", unitCommand)));
        return true;
    }

    /// <summary>Excludes the multi-key overloads, which reach a different wire command.</summary>
    private static bool TryGetRedisSingleKeyOperation(
        EquatableArray<ParameterSpec> parameters,
        string command,
        out RedisOperationSpec operation)
    {
        operation = default;
        if (IsRedisParameter(parameters, 0, RedisKeyArrayType))
            return false;

        operation = new RedisOperationSpec(command);
        return true;
    }

    private static RedisOperationSpec RedisWhenOperation(
        EquatableArray<ParameterSpec> parameters,
        string command,
        string alternateCommand,
        string whenMember)
        => RedisDiscriminatedOperation(parameters, RedisWhenType, whenMember, command, alternateCommand);

    private static RedisOperationSpec RedisOrderOperation(
        EquatableArray<ParameterSpec> parameters,
        string ascendingCommand,
        string descendingCommand)
        => RedisDiscriminatedOperation(parameters, RedisOrderType, "Descending", ascendingCommand, descendingCommand);

    /// <summary>
    /// Builds the call-site test that selects <paramref name="alternateCommand"/>. The test is an
    /// enum comparison, which the call site evaluates without allocating. An overload without the
    /// discriminating parameter always reaches <paramref name="command"/>.
    /// </summary>
    private static RedisOperationSpec RedisDiscriminatedOperation(
        EquatableArray<ParameterSpec> parameters,
        string enumTypeName,
        string enumMember,
        string command,
        string alternateCommand)
    {
        var index = IndexOfRedisParameter(parameters, enumTypeName);
        return index < 0
            ? new RedisOperationSpec(command)
            : new RedisOperationSpec(
                command,
                RedisBranches(new RedisCommandBranch(
                    parameters[index].Name + " == " + enumTypeName + "." + enumMember,
                    alternateCommand)));
    }

    /// <summary>
    /// A null expiry reaches PERSIST, and StackExchange.Redis picks the second-precision command
    /// when the value carries no whole milliseconds — <c>TimeSpan.FromTicks(TimeSpan.TicksPerSecond + 1)</c>
    /// still reaches EXPIRE, so the test is the millisecond component rather than the tick
    /// remainder. Both tests read the argument the call site already holds.
    /// </summary>
    private static bool TryGetRedisExpireOperation(
        EquatableArray<ParameterSpec> parameters,
        out RedisOperationSpec operation)
    {
        operation = default;
        if (parameters.Length < 2)
            return false;

        var expiry = parameters[1].Name;
        switch (parameters[1].TypeName)
        {
            case "global::System.TimeSpan?":
                operation = new RedisOperationSpec(
                    "PEXPIRE",
                    RedisBranches(
                        new RedisCommandBranch(expiry + " is null", "PERSIST"),
                        new RedisCommandBranch(expiry + ".Value.Milliseconds == 0", "EXPIRE")));
                return true;
            case "global::System.DateTime?":
                operation = new RedisOperationSpec(
                    "PEXPIREAT",
                    RedisBranches(
                        new RedisCommandBranch(expiry + " is null", "PERSIST"),
                        new RedisCommandBranch(expiry + ".Value.Millisecond == 0", "EXPIREAT")));
                return true;
            default:
                return false;
        }
    }

    private static EquatableArray<RedisCommandBranch> RedisBranches(params RedisCommandBranch[] branches)
        => ImmutableArray.Create(branches).AsEquatableArray();

    private static bool IsRedisParameter(EquatableArray<ParameterSpec> parameters, int index, string typeName)
        => index < parameters.Length &&
           string.Equals(parameters[index].TypeName, typeName, StringComparison.Ordinal);

    private static int IndexOfRedisParameter(EquatableArray<ParameterSpec> parameters, string typeName)
    {
        for (var index = 0; index < parameters.Length; index++)
        {
            if (string.Equals(parameters[index].TypeName, typeName, StringComparison.Ordinal))
                return index;
        }

        return -1;
    }

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
