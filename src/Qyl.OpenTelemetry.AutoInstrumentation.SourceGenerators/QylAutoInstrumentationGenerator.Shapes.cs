using ANcpLua.Roslyn.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private static bool TryGetDbCommandParameters(IMethodSymbol symbol, string methodName, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (string.Equals(methodName, "ExecuteReader", StringComparison.Ordinal))
            return TryGetDbExecuteReaderParameters(symbol, allowCancellationToken: false, out parameters);

        if (string.Equals(methodName, "ExecuteReaderAsync", StringComparison.Ordinal))
            return TryGetDbExecuteReaderParameters(symbol, allowCancellationToken: true, out parameters);

        if (string.Equals(methodName, "ExecuteScalar", StringComparison.Ordinal) ||
            string.Equals(methodName, "ExecuteNonQuery", StringComparison.Ordinal))
            return TryGetNoParameters(symbol, out parameters);

        if (string.Equals(methodName, "ExecuteScalarAsync", StringComparison.Ordinal) ||
            string.Equals(methodName, "ExecuteNonQueryAsync", StringComparison.Ordinal))
            return TryGetOptionalCancellationTokenParameters(symbol, out parameters);

        return false;
    }

    private static bool TryGetDbCommandReturn(IMethodSymbol symbol, string methodName, bool isAsync, out string returnType)
    {
        returnType = CleanTypeName(symbol.ReturnType, symbol);
        if (string.Equals(methodName, "ExecuteNonQuery", StringComparison.Ordinal))
            return symbol.ReturnType.SpecialType is SpecialType.System_Int32;

        if (string.Equals(methodName, "ExecuteScalar", StringComparison.Ordinal))
            return symbol.ReturnType.SpecialType is SpecialType.System_Object;

        if (string.Equals(methodName, "ExecuteReader", StringComparison.Ordinal))
            return InheritsFromOrIs(symbol.ReturnType, "global::System.Data.Common.DbDataReader");

        if (!isAsync || !TryGetTaskResult(symbol.ReturnType, out var taskResult))
            return false;

        if (string.Equals(methodName, "ExecuteNonQueryAsync", StringComparison.Ordinal))
            return taskResult.SpecialType is SpecialType.System_Int32;

        if (string.Equals(methodName, "ExecuteScalarAsync", StringComparison.Ordinal))
            return taskResult.SpecialType is SpecialType.System_Object;

        if (string.Equals(methodName, "ExecuteReaderAsync", StringComparison.Ordinal))
            return InheritsFromOrIs(taskResult, "global::System.Data.Common.DbDataReader");

        return false;
    }

    private static bool TryGetHttpWebRequestReturn(IMethodSymbol symbol, string methodName, bool isAsync, out string returnType)
    {
        returnType = CleanTypeName(symbol.ReturnType, symbol);
        if (!isAsync)
        {
            if (string.Equals(methodName, "GetResponse", StringComparison.Ordinal))
                return InheritsFromOrIs(symbol.ReturnType, "global::System.Net.WebResponse");

            if (string.Equals(methodName, "GetRequestStream", StringComparison.Ordinal))
                return InheritsFromOrIs(symbol.ReturnType, "global::System.IO.Stream");

            return false;
        }

        if (!TryGetTaskResult(symbol.ReturnType, out var taskResult))
            return false;

        if (string.Equals(methodName, "GetResponseAsync", StringComparison.Ordinal))
            return InheritsFromOrIs(taskResult, "global::System.Net.WebResponse");

        if (string.Equals(methodName, "GetRequestStreamAsync", StringComparison.Ordinal))
            return InheritsFromOrIs(taskResult, "global::System.IO.Stream");

        return false;
    }

    private static InterceptorTarget HttpTarget(IMethodSymbol symbol, string methodName, string returnType, EquatableArray<ParameterSpec> parameters)
        => new(
            InterceptorKind.HttpClient,
            "signals.traces.HTTPCLIENT",
            "HTTPCLIENT",
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            false,
            AdditionalContractKeys: BuildContractKeys("signals.metrics.HTTPCLIENT"));

    private static string GetDbTraceContractKey(string instrumentationId)
        => instrumentationId switch
        {
            "ADONET" => "signals.traces.ADONET",
            "MYSQLCONNECTOR" => "signals.traces.MYSQLCONNECTOR",
            "MYSQLDATA" => "signals.traces.MYSQLDATA",
            "NPGSQL" => "signals.traces.NPGSQL",
            "ORACLEMDA" => "signals.traces.ORACLEMDA",
            "SQLCLIENT" => "signals.traces.SQLCLIENT",
            "SQLITE" => "signals.traces.SQLITE",
            _ => "signals.traces.ADONET",
        };

    private static EquatableArray<string> GetDbMetricContractKeys(string instrumentationId)
        => instrumentationId switch
        {
            "NPGSQL" => BuildContractKeys("signals.metrics.NPGSQL"),
            "SQLCLIENT" => BuildContractKeys("signals.metrics.SQLCLIENT"),
            _ => default,
        };

    private static EquatableArray<string> BuildContractKeys(params string[] contractKeys)
        => contractKeys.ToEquatableArray();

    private static ulong InterceptorKinds(params InterceptorKind[] kinds)
    {
        var mask = 0UL;
        foreach (var kind in kinds)
            mask |= GetInterceptorKindMask(kind);

        return mask;
    }

    private static ulong GetInterceptorKindMask(InterceptorKind kind)
    {
        var ordinal = (int)kind;
        if ((uint)ordinal >= 64)
            throw new InvalidOperationException("InterceptorKind ordinal is outside the descriptor bitmask range: " + kind);

        return 1UL << ordinal;
    }

    private static bool TryGetSendShape(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is < 1 or > 3 ||
            !IsType(symbol.Parameters[0].Type, "global::System.Net.Http.HttpRequestMessage"))
        {
            return false;
        }

        if (symbol.Parameters.Length is 1)
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 2)
        {
            if (IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken") ||
                IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption"))
            {
                parameters = BuildParameters(symbol);
                return true;
            }
        }

        if (symbol.Parameters.Length is 3 &&
            IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption") &&
            IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        return false;
    }

    private static bool TryGetRequestUriShape(IMethodSymbol symbol, bool allowCompletionOption, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is < 1 or > 3)
            return false;

        var firstIsString = IsType(symbol.Parameters[0].Type, "global::System.String");
        var firstIsUri = IsType(symbol.Parameters[0].Type, "global::System.Uri");
        if (!firstIsString && !firstIsUri)
            return false;

        if (symbol.Parameters.Length is 1)
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 2)
        {
            if (IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken") ||
                (allowCompletionOption && IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption")))
            {
                parameters = BuildParameters(symbol);
                return true;
            }
        }

        if (allowCompletionOption &&
            symbol.Parameters.Length is 3 &&
            IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption") &&
            IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        return false;
    }

    private static bool TryGetRequestUriContentShape(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is not (2 or 3))
            return false;

        var firstIsString = IsType(symbol.Parameters[0].Type, "global::System.String");
        var firstIsUri = IsType(symbol.Parameters[0].Type, "global::System.Uri");
        if ((!firstIsString && !firstIsUri) ||
            !IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpContent"))
        {
            return false;
        }

        if (symbol.Parameters.Length is 2 || IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        return false;
    }

    private static bool TryGetNoParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = default;
            return true;
        }

        parameters = default;
        return false;
    }

    private static bool TryGetOptionalCancellationTokenParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = default;
            return true;
        }

        if (symbol.Parameters.Length is 1 && IsType(symbol.Parameters[0].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        parameters = default;
        return false;
    }

    private static bool TryGetDbExecuteReaderParameters(IMethodSymbol symbol, bool allowCancellationToken, out EquatableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = default;
            return true;
        }

        if (symbol.Parameters.Length is 1)
        {
            if (IsType(symbol.Parameters[0].Type, "global::System.Data.CommandBehavior") ||
                (allowCancellationToken && IsType(symbol.Parameters[0].Type, "global::System.Threading.CancellationToken")))
            {
                parameters = BuildParameters(symbol);
                return true;
            }
        }

        if (allowCancellationToken &&
            symbol.Parameters.Length is 2 &&
            IsType(symbol.Parameters[0].Type, "global::System.Data.CommandBehavior") &&
            IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        parameters = default;
        return false;
    }

    private static bool TryGetKafkaProduceParameters(IMethodSymbol symbol, bool isAsync, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is not (2 or 3))
            return false;

        var firstIsTopic = IsType(symbol.Parameters[0].Type, "global::System.String") ||
                           IsType(symbol.Parameters[0].Type, "global::Confluent.Kafka.TopicPartition");
        if (!firstIsTopic || !IsConstructedGeneric(symbol.Parameters[1].Type, "Confluent.Kafka", "Message`2"))
            return false;

        if (symbol.Parameters.Length is 3)
        {
            if (isAsync)
            {
                if (!IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
                    return false;
            }
            else if (!IsKafkaDeliveryReportHandler(symbol.Parameters[2].Type))
            {
                return false;
            }
        }

        parameters = BuildParameters(symbol);
        return true;
    }

    private static bool IsKafkaDeliveryReportHandler(ITypeSymbol? symbol)
        => symbol is INamedTypeSymbol
        {
            ConstructedFrom.MetadataName: "Action`1",
            TypeArguments.Length: 1,
        } named &&
        string.Equals(named.ConstructedFrom.ContainingNamespace.ToDisplayString(), "System", StringComparison.Ordinal) &&
        IsConstructedGeneric(named.TypeArguments[0], "Confluent.Kafka", "DeliveryReport`2");

    private static bool TryGetKafkaConsumeParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is 0)
            return true;

        if (symbol.Parameters.Length is not 1)
            return false;

        if (IsType(symbol.Parameters[0].Type, "global::System.Threading.CancellationToken") ||
            IsType(symbol.Parameters[0].Type, "global::System.TimeSpan") ||
            symbol.Parameters[0].Type.SpecialType is SpecialType.System_Int32)
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        return false;
    }

    private static bool TryGetRedisCommandParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (!CanEmitRedisParameters(symbol))
            return false;

        if (string.Equals(symbol.Name, "ExecuteAsync", StringComparison.Ordinal))
        {
            if (symbol.Parameters.Length is 0 ||
                !IsType(symbol.Parameters[0].Type, "global::System.String"))
            {
                return false;
            }

            parameters = BuildParameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 0)
            return false;

        if (!IsType(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey") &&
            !IsArrayOf(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey"))
        {
            return false;
        }

        parameters = BuildParameters(symbol);
        return true;
    }

    private static bool CanEmitRedisParameters(IMethodSymbol symbol)
    {
        foreach (var parameter in symbol.Parameters)
        {
            if (parameter.RefKind is not RefKind.None and not RefKind.In)
                return false;
        }

        return true;
    }

    private static bool TryGetRedisStringGetParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length is < 1 or > 2)
            return false;

        if (!IsType(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey") &&
            !IsArrayOf(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey"))
        {
            return false;
        }

        if (symbol.Parameters.Length is 2 &&
            !IsType(symbol.Parameters[1].Type, "global::StackExchange.Redis.CommandFlags"))
        {
            return false;
        }

        parameters = BuildParameters(symbol);
        return true;
    }

    private static bool TryGetEfCoreSaveChangesParameters(IMethodSymbol symbol, bool allowCancellationToken, out EquatableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = default;
            return true;
        }

        if (symbol.Parameters.Length is 1)
        {
            if (symbol.Parameters[0].Type.SpecialType is SpecialType.System_Boolean ||
                (allowCancellationToken && IsType(symbol.Parameters[0].Type, "global::System.Threading.CancellationToken")))
            {
                parameters = BuildParameters(symbol);
                return true;
            }
        }

        if (allowCancellationToken &&
            symbol.Parameters.Length is 2 &&
            symbol.Parameters[0].Type.SpecialType is SpecialType.System_Boolean &&
            IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        parameters = default;
        return false;
    }

    private static bool TryGetRabbitMqBasicPublishParameters(IMethodSymbol symbol, out EquatableArray<ParameterSpec> parameters)
    {
        parameters = default;
        if (symbol.Parameters.Length < 3)
            return false;

        if (IsType(symbol.Parameters[0].Type, "global::System.String") &&
            IsType(symbol.Parameters[1].Type, "global::System.String"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        if (symbol.Parameters[0].Type is INamedTypeSymbol publicationAddress &&
            IsTypeByMetadata(publicationAddress, "RabbitMQ.Client", "PublicationAddress"))
        {
            parameters = BuildParameters(symbol);
            return true;
        }

        return false;
    }

    private static bool TryGetReducedExtensionReceiverType(IMethodSymbol symbol, out ITypeSymbol receiverType)
    {
        if (symbol.ReducedFrom is { Parameters.Length: > 0 } original)
        {
            receiverType = original.Parameters[0].Type;
            return true;
        }

        receiverType = symbol.ContainingType;
        return false;
    }

    private static string GetReducedExtensionContainingType(IMethodSymbol symbol)
        => symbol.ReducedFrom is null
            ? string.Empty
            : CleanTypeName(symbol.ReducedFrom.ContainingType);

    private static bool CanEmitByValueOrInParameters(IMethodSymbol symbol)
    {
        foreach (var parameter in symbol.Parameters)
        {
            if (parameter.RefKind is not RefKind.None and not RefKind.In)
                return false;
        }

        return true;
    }

    private static string GetTypeParameterList(IMethodSymbol symbol)
    {
        var genericSymbol = GetGenericMethodForEmission(symbol);
        if (genericSymbol.TypeParameters.Length is 0)
            return string.Empty;

        var builder = new StringBuilder();
        builder.Append('<');
        for (var i = 0; i < genericSymbol.TypeParameters.Length; i++)
        {
            if (i > 0)
                builder.Append(", ");

            builder.Append(genericSymbol.TypeParameters[i].Name);
        }

        builder.Append('>');
        return builder.ToString();
    }

    private static string GetConstraintClauses(IMethodSymbol symbol)
    {
        var genericSymbol = GetGenericMethodForEmission(symbol);
        if (genericSymbol.TypeParameters.Length is 0)
            return string.Empty;

        var builder = new StringBuilder();
        foreach (var typeParameter in genericSymbol.TypeParameters)
        {
            var constraintClause = GetConstraintClause(typeParameter);
            if (string.IsNullOrWhiteSpace(constraintClause))
                continue;

            builder.Append(' ');
            builder.Append(constraintClause);
        }

        return builder.ToString();
    }

    private static string GetTypeParameterListFromVisibleTypes(IMethodSymbol symbol, ITypeSymbol receiverType)
    {
        var names = new List<string>();
        AddTypeParameterNames(receiverType, names);
        AddTypeParameterNames(symbol.ReturnType, names);

        foreach (var parameter in symbol.Parameters)
            AddTypeParameterNames(parameter.Type, names);

        return names.Count is 0
            ? string.Empty
            : "<" + string.Join(", ", names) + ">";
    }

    private static string GetTypeParameterListFromFormattedTypes(
        string receiverType,
        string returnType,
        EquatableArray<ParameterSpec> parameters)
    {
        var names = new List<string>();
        AddFormattedTypeParameterNames(receiverType, names);
        AddFormattedTypeParameterNames(returnType, names);

        foreach (var parameter in parameters)
            AddFormattedTypeParameterNames(parameter.TypeName, names);

        return names.Count is 0
            ? string.Empty
            : "<" + string.Join(", ", names) + ">";
    }

    private static void AddFormattedTypeParameterNames(string typeName, List<string> names)
    {
        for (var i = 0; i < typeName.Length; i++)
        {
            if (typeName[i] is not 'T' ||
                i > 0 && typeName[i - 1] is not '<' and not ',' and not ' ')
            {
                continue;
            }

            var end = i + 1;
            while (end < typeName.Length && (char.IsLetterOrDigit(typeName[end]) || typeName[end] == '_'))
                end++;

            var candidate = typeName.Substring(i, end - i);
            if (candidate.Length > 1 && !names.Contains(candidate))
                names.Add(candidate);
        }
    }

    private static void AddTypeParameterNames(ITypeSymbol symbol, List<string> names)
    {
        if (symbol is ITypeParameterSymbol typeParameter)
        {
            if (!names.Contains(typeParameter.Name))
                names.Add(typeParameter.Name);

            return;
        }

        if (symbol is IArrayTypeSymbol array)
        {
            AddTypeParameterNames(array.ElementType, names);
            return;
        }

        if (symbol is INamedTypeSymbol named)
        {
            foreach (var typeArgument in named.TypeArguments)
                AddTypeParameterNames(typeArgument, names);
        }
    }

    private static IMethodSymbol GetGenericMethodForEmission(IMethodSymbol symbol)
        => symbol.TypeParameters.Length > 0
            ? symbol
            : symbol.ReducedFrom is { TypeParameters.Length: > 0 } reducedFrom
                ? reducedFrom
                : symbol;

    private static string GetConstraintClause(ITypeParameterSymbol typeParameter)
    {
        var constraints = ImmutableArray.CreateBuilder<string>();

        if (typeParameter.HasUnmanagedTypeConstraint)
            constraints.Add("unmanaged");
        else if (typeParameter.HasValueTypeConstraint)
            constraints.Add("struct");
        else if (typeParameter.HasReferenceTypeConstraint)
            constraints.Add("class");
        else if (typeParameter.HasNotNullConstraint)
            constraints.Add("notnull");

        foreach (var constraintType in typeParameter.ConstraintTypes)
            constraints.Add(CleanTypeName(constraintType));

        if (typeParameter.HasConstructorConstraint)
            constraints.Add("new()");

        return constraints.Count is 0
            ? string.Empty
            : "where " + typeParameter.Name + " : " + string.Join(", ", constraints);
    }

    private static EquatableArray<ParameterSpec> BuildParameters(IMethodSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterSpec>(symbol.Parameters.Length);
        for (var i = 0; i < symbol.Parameters.Length; i++)
            builder.Add(new ParameterSpec(
                CleanTypeName(symbol.Parameters[i].Type, symbol),
                "p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetDefaultValueExpression(symbol.Parameters[i]),
                symbol.Parameters[i].IsParams,
                symbol.Parameters[i].RefKind));

        return builder.ToImmutable().AsEquatableArray();
    }

    private static string GetDefaultValueExpression(IParameterSymbol parameter)
    {
        if (!parameter.IsOptional)
            return string.Empty;

        if (!parameter.HasExplicitDefaultValue)
            return "default";

        if (parameter.ExplicitDefaultValue is null)
            return parameter.Type.IsValueType ? "default" : "null";

        if (parameter.Type.SpecialType is SpecialType.System_Boolean)
            return (bool)parameter.ExplicitDefaultValue ? "true" : "false";

        if (parameter.Type.SpecialType is SpecialType.System_Int32)
            return ((int)parameter.ExplicitDefaultValue).ToString(System.Globalization.CultureInfo.InvariantCulture);

        if (parameter.Type.SpecialType is SpecialType.System_String)
            return "\"" + parameter.ExplicitDefaultValue.ToString().Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";

        return "default";
    }

    private static bool IsTaskOf(ITypeSymbol? symbol, string resultFullyQualifiedName)
        => TryGetTaskResult(symbol, out var result) && IsType(result, resultFullyQualifiedName);

    private static bool IsConstructedFrom(ITypeSymbol? symbol, string fullyQualifiedConstructedFromName)
        => symbol is INamedTypeSymbol named && IsType(named.ConstructedFrom, fullyQualifiedConstructedFromName);

    private static bool IsConstructedGeneric(ITypeSymbol? symbol, string namespaceName, string metadataName)
        => symbol is INamedTypeSymbol named &&
           string.Equals(named.ConstructedFrom.MetadataName, metadataName, StringComparison.Ordinal) &&
           string.Equals(named.ConstructedFrom.ContainingNamespace.ToDisplayString(), namespaceName, StringComparison.Ordinal);

    private static bool IsArrayOf(ITypeSymbol? symbol, string elementFullyQualifiedName)
        => symbol is IArrayTypeSymbol array && IsType(array.ElementType, elementFullyQualifiedName);

    private static bool IsLoggerFormatter(ITypeSymbol? symbol)
        => symbol is INamedTypeSymbol
        {
            Name: "Func",
            TypeArguments.Length: 3,
        } named &&
        IsType(named.TypeArguments[1], "global::System.Exception") &&
        IsType(named.TypeArguments[2], "global::System.String");

    private static bool IsTask(ITypeSymbol? symbol)
        => IsType(symbol, "global::System.Threading.Tasks.Task");

    private static bool IsValueTask(ITypeSymbol? symbol)
        => IsType(symbol, "global::System.Threading.Tasks.ValueTask");

    private static bool TryGetTaskResult(ITypeSymbol? symbol, out ITypeSymbol result)
    {
        result = null!;
        if (symbol is not INamedTypeSymbol named ||
            !IsType(named.ConstructedFrom, "global::System.Threading.Tasks.Task<TResult>") ||
            named.TypeArguments.Length is not 1)
        {
            return false;
        }

        result = named.TypeArguments[0];
        return true;
    }

    private static bool InheritsFromOrIs(ITypeSymbol? symbol, string fullyQualifiedName)
    {
        for (var current = symbol; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (IsType(current, fullyQualifiedName))
                return true;
        }

        return false;
    }

    private static bool InheritsFromConstructedGeneric(ITypeSymbol? symbol, string fullyQualifiedConstructedFromName)
    {
        for (var current = symbol; current is not null; current = (current as INamedTypeSymbol)?.BaseType)
        {
            if (current is INamedTypeSymbol named && IsType(named.ConstructedFrom, fullyQualifiedConstructedFromName))
                return true;
        }

        return false;
    }

    private static bool IsOrImplementsConstructedGeneric(ITypeSymbol? symbol, string namespaceName, string metadataName)
    {
        if (symbol is not INamedTypeSymbol named)
            return false;

        if (IsConstructedGeneric(named, namespaceName, metadataName))
            return true;

        foreach (var interfaceType in named.AllInterfaces)
        {
            if (IsConstructedGeneric(interfaceType, namespaceName, metadataName))
                return true;
        }

        return false;
    }

    private static bool IsOrImplementsType(ITypeSymbol? symbol, string namespaceName, string metadataName)
    {
        if (symbol is not INamedTypeSymbol named)
            return false;

        if (IsTypeByMetadata(named, namespaceName, metadataName))
            return true;

        foreach (var interfaceType in named.AllInterfaces)
        {
            if (IsTypeByMetadata(interfaceType, namespaceName, metadataName))
                return true;
        }

        return false;
    }

    private static bool IsTypeByMetadata(INamedTypeSymbol symbol, string namespaceName, string metadataName)
        => string.Equals(symbol.MetadataName, metadataName, StringComparison.Ordinal) &&
           string.Equals(symbol.ContainingNamespace.ToDisplayString(), namespaceName, StringComparison.Ordinal);

    private static bool IsType(ITypeSymbol? symbol, string fullyQualifiedName)
    {
        if (symbol is null)
            return false;

        if (fullyQualifiedName is "global::System.String")
            return symbol.SpecialType is SpecialType.System_String;

        if (fullyQualifiedName is "global::System.Byte[]")
            return symbol is IArrayTypeSymbol { ElementType.SpecialType: SpecialType.System_Byte, Rank: 1 };

        if (fullyQualifiedName is "global::System.Object")
            return symbol.SpecialType is SpecialType.System_Object;

        if (fullyQualifiedName is "global::System.Int32")
            return symbol.SpecialType is SpecialType.System_Int32;

        var display = CleanTypeName(symbol);
        return string.Equals(display, fullyQualifiedName, StringComparison.Ordinal);
    }

    private static string GetDbInstrumentationId(ITypeSymbol type)
    {
        var display = CleanTypeName(type);
        if (display.StartsWithOrdinal("global::Microsoft.Data.SqlClient.") ||
            display.StartsWithOrdinal("global::System.Data.SqlClient."))
        {
            return "SQLCLIENT";
        }

        if (display.StartsWithOrdinal("global::Microsoft.Data.Sqlite."))
            return "SQLITE";

        if (display.StartsWithOrdinal("global::Npgsql."))
            return "NPGSQL";

        if (display.StartsWithOrdinal("global::MySqlConnector."))
            return "MYSQLCONNECTOR";

        if (display.StartsWithOrdinal("global::MySql.Data."))
            return "MYSQLDATA";

        if (display.StartsWithOrdinal("global::Oracle.ManagedDataAccess."))
            return "ORACLEMDA";

        return "ADONET";
    }

    private static string CleanTypeName(ITypeSymbol symbol)
        => symbol.ToDisplayString(s_fullyQualifiedFormat);

    private static string CleanTypeName(ITypeSymbol symbol, IMethodSymbol method)
    {
        var genericMethod = GetGenericMethodForEmission(method);
        return genericMethod.TypeParameters.Length is 0
            ? CleanTypeName(symbol)
            : CleanTypeName(symbol, genericMethod.TypeParameters, genericMethod.TypeArguments);
    }

    private static string CleanTypeName(
        ITypeSymbol symbol,
        ImmutableArray<ITypeParameterSymbol> typeParameters,
        ImmutableArray<ITypeSymbol> typeArguments)
    {
        for (var i = 0; i < typeArguments.Length && i < typeParameters.Length; i++)
        {
            if (SymbolEqualityComparer.Default.Equals(symbol, typeArguments[i]))
                return typeParameters[i].Name;
        }

        switch (symbol)
        {
            case ITypeParameterSymbol typeParameter:
                return typeParameter.Name;
            case IArrayTypeSymbol array:
                return CleanTypeName(array.ElementType, typeParameters, typeArguments) + "[]";
            case INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: > 0 } named:
            {
                var constructedName = named.ConstructedFrom.ToDisplayString(s_fullyQualifiedFormat);
                var genericStart = constructedName.IndexOf('<');
                var typeName = genericStart < 0 ? constructedName : constructedName.Substring(0, genericStart);
                var arguments = named.TypeArguments
                    .Select(typeArgument => CleanTypeName(typeArgument, typeParameters, typeArguments));
                return typeName + "<" + string.Join(", ", arguments) + ">";
            }
            default:
                return CleanTypeName(symbol);
        }
    }

    private static string GetGrpcStreamReaderHelperType(InterceptedInvocation[] invocations)
    {
        var helperType = "";
        foreach (var invocation in invocations)
        {
            if (invocation.Target.Kind is not (InterceptorKind.GrpcNetClientAsyncServerStreamingCall or
                    InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall))
            {
                continue;
            }

            var descriptor = GetEmissionDescriptor(invocation.Target).GrpcClientBody;
            if (!descriptor.IsDefined)
                throw new InvalidOperationException("gRPC streaming interceptor kind has no gRPC body descriptor: " + invocation.Target.Kind);
            if (string.IsNullOrEmpty(helperType))
            {
                helperType = descriptor.HelperType;
                continue;
            }

            if (!string.Equals(helperType, descriptor.HelperType, StringComparison.Ordinal))
                throw new InvalidOperationException("gRPC streaming helper types disagree.");
        }

        return helperType;
    }

    private static void AppendStringLiteral(StringBuilder builder, string value)
    {
        builder.Append('"');
        builder.Append(value.Replace("\\", @"\\").Replace("\"", "\\\""));
        builder.Append('"');
    }
}
