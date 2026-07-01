using ANcpLua.Roslyn.Utilities;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private enum InterceptorKind
    {
        HttpClient,
        HttpWebRequest,
        AspNetCoreEndpointMap,
        MeterProviderBuilderAddMeter,
        AzureClient,
        ElasticsearchClient,
        ElasticTransport,
        WcfClient,
        GrpcNetClientAsyncUnaryCall,
        GrpcNetClientAsyncServerStreamingCall,
        GrpcNetClientAsyncClientStreamingCall,
        GrpcNetClientAsyncDuplexStreamingCall,
        KafkaProducer,
        KafkaConsumer,
        MassTransitMessageOperation,
        NServiceBusMessageOperation,
        QuartzJobExecute,
        StackExchangeRedisCommandAsync,
        GraphQlDocumentExecuter,
        MongoDbCollection,
        RabbitMqBasicPublish,
        ILoggerExtensionLog,
        ILoggerLog,
        NLogLogger,
        Log4NetLogger,
        EntityFrameworkCoreDbContext,
        EntityFrameworkCoreQueryable,
        DbCommand,
    }

    private enum LoggerInterceptorBodyKind
    {
        None,
        ILoggerLog,
        LoggerExtensionLog,
    }

    private enum TraceStartActivityArgumentKind
    {
        None,
        InstrumentationIdAndTargetMethodName,
        ReceiverTypeAndTargetMethodName,
        RedisOperationName,
        TargetMethodName,
        RabbitMqExchange,
    }

    private enum TraceDurationMetricArgumentKind
    {
        None,
        TargetMethodName,
    }

    private enum TraceActivityEnrichmentArgumentKind
    {
        None,
        GraphQlExecutionOptions,
    }

    private enum TraceAsyncObservationCondition
    {
        Always,
        AsyncWithByRefParameters,
    }

    private enum TraceMethodPrefixKind
    {
        Default,
        InstrumentationIdAndTargetMethodName,
    }

    private enum GrpcClientCallShape
    {
        None,
        Unary,
        ServerStreaming,
        ClientStreaming,
        DuplexStreaming,
    }

    private readonly record struct InterceptorEmissionDescriptor(
        InterceptorKind Kind,
        InterceptorBodyDescriptor Body);

    /// <summary>
    /// Closed set of interceptor body shapes. Exactly-one-body-per-descriptor is structural:
    /// an emission descriptor holds a single <see cref="InterceptorBodyDescriptor"/> and the
    /// emitter dispatches on its concrete type.
    /// </summary>
    private abstract record InterceptorBodyDescriptor;

    private sealed record GrpcClientBodyDescriptor(
        GrpcClientCallShape Shape,
        string MethodPrefix,
        string ReceiverName,
        string HelperType) : InterceptorBodyDescriptor;

    private sealed record DbCommandBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string HelperType,
        string MetricsType,
        string GetTimestampMethod,
        string StartActivityMethod,
        string ObserveAsyncMethod,
        string RecordExceptionMethod,
        string RecordDurationMethod) : InterceptorBodyDescriptor;

    private sealed record HttpWebRequestBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string RequestType,
        string HelperType,
        string GetStartTimeUtcMethod,
        string StartActivityMethod,
        string RecordResultMethod,
        string RecordExceptionMethod) : InterceptorBodyDescriptor;

    private sealed record MeterProviderBuilderBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string EnabledMeterNamesExpression) : InterceptorBodyDescriptor;

    private sealed record LoggerBodyDescriptor(
        LoggerInterceptorBodyKind Kind,
        string MethodPrefix,
        string HelperType) : InterceptorBodyDescriptor;

    private sealed record ExternalLoggerBodyDescriptor(
        string HelperType,
        string DomainExpression) : InterceptorBodyDescriptor;

    private sealed record ForwardingInterceptorBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string HelperType,
        string HelperMethodName = "",
        string ReceiverTypeOverride = "") : InterceptorBodyDescriptor;

    private readonly record struct TraceRuntimeHelperDescriptor(
        string HelperType,
        string StartActivityMethod,
        string RecordExceptionMethod,
        TraceStartActivityArgumentKind StartActivityArguments = TraceStartActivityArgumentKind.None,
        bool IsDefined = true)
    {
        public string GetRecordExceptionStatement()
            => HelperType + "." + RecordExceptionMethod + "(activity, exception);";
    }

    private readonly record struct TraceDurationMetricDescriptor(
        string HelperType,
        string GetTimestampMethod,
        string RecordDurationMethod,
        TraceDurationMetricArgumentKind RecordDurationArguments = TraceDurationMetricArgumentKind.None,
        bool IsDefined = true)
    {
        public void AppendMetricStartStatement(StringBuilder builder)
        {
            builder.Append("            var metricStart = ");
            builder.Append(HelperType);
            builder.Append('.');
            builder.Append(GetTimestampMethod);
            builder.AppendLine("();");
        }

        public void AppendRecordDurationStatement(StringBuilder builder, in InterceptorTarget target)
        {
            builder.Append("                ");
            builder.Append(HelperType);
            builder.Append('.');
            builder.Append(RecordDurationMethod);
            builder.Append("(metricStart");
            AppendRecordDurationArguments(builder, in target);
            builder.AppendLine(");");
        }

        private void AppendRecordDurationArguments(StringBuilder builder, in InterceptorTarget target)
        {
            switch (RecordDurationArguments)
            {
                case TraceDurationMetricArgumentKind.None:
                    return;
                case TraceDurationMetricArgumentKind.TargetMethodName:
                    builder.Append(", ");
                    QylAutoInstrumentationGenerator.AppendStringLiteral(builder, target.MethodName);
                    return;
                default:
                    throw new InvalidOperationException("Unknown trace duration metric argument kind: " + RecordDurationArguments);
            }
        }
    }

    private readonly record struct TraceActivityEnrichmentDescriptor(
        string HelperType,
        string EnrichMethod,
        TraceActivityEnrichmentArgumentKind Arguments = TraceActivityEnrichmentArgumentKind.None,
        bool IsDefined = true)
    {
        public void Append(StringBuilder builder, in InterceptorTarget target)
        {
            builder.AppendLine("            if (activity is not null)");
            builder.AppendLine("            {");
            builder.Append("                ");
            builder.Append(HelperType);
            builder.Append('.');
            builder.Append(EnrichMethod);
            builder.Append("(activity");
            AppendArguments(builder, in target);
            builder.AppendLine(");");
            builder.AppendLine("            }");
        }

        private void AppendArguments(StringBuilder builder, in InterceptorTarget target)
        {
            switch (Arguments)
            {
                case TraceActivityEnrichmentArgumentKind.None:
                    return;
                case TraceActivityEnrichmentArgumentKind.GraphQlExecutionOptions:
                    builder.Append(", ");
                    QylAutoInstrumentationGenerator.AppendGraphQlOperationNameExpression(builder, in target);
                    builder.Append(", ");
                    QylAutoInstrumentationGenerator.AppendGraphQlDocumentCaptureExpression(builder, in target);
                    return;
                default:
                    throw new InvalidOperationException("Unknown trace activity enrichment argument kind: " + Arguments);
            }
        }
    }

    private readonly record struct TraceAsyncObservationDescriptor(
        string ObserveAsyncMethod,
        TraceAsyncObservationCondition Condition = TraceAsyncObservationCondition.Always,
        bool IsDefined = true)
    {
        public bool AppliesTo(in InterceptorTarget target)
        {
            switch (Condition)
            {
                case TraceAsyncObservationCondition.Always:
                    return true;
                case TraceAsyncObservationCondition.AsyncWithByRefParameters:
                    return target.IsAsync && QylAutoInstrumentationGenerator.HasByRefParameters(target.Parameters);
                default:
                    throw new InvalidOperationException("Unknown trace async observation condition: " + Condition);
            }
        }
    }

    private sealed record TraceInterceptorBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        TraceMethodPrefixKind MethodPrefixKind = TraceMethodPrefixKind.Default,
        TraceRuntimeHelperDescriptor RuntimeHelper = default,
        TraceDurationMetricDescriptor DurationMetric = default,
        TraceActivityEnrichmentDescriptor ActivityEnrichment = default,
        TraceAsyncObservationDescriptor AsyncObservation = default) : InterceptorBodyDescriptor
    {
        public void AppendStartActivity(StringBuilder builder, in InterceptorTarget target)
        {
            builder.Append(RuntimeHelper.HelperType);
            builder.Append('.');
            builder.Append(RuntimeHelper.StartActivityMethod);
            builder.Append('(');
            QylAutoInstrumentationGenerator.AppendTraceStartActivityArguments(builder, in target, RuntimeHelper.StartActivityArguments);
            builder.Append(')');
        }

        public string GetRecordExceptionStatement()
            => RuntimeHelper.GetRecordExceptionStatement();
    }

    private readonly record struct InterceptorMatcherDescriptor
    {
        private readonly SymbolInterceptorMatcher? _symbolMatcher;
        private readonly ReceiverInterceptorMatcher? _receiverMatcher;

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            SymbolInterceptorMatcher matcher)
        {
            Name = name;
            ReceiverTypePattern = receiverTypePattern;
            _symbolMatcher = matcher;
            _receiverMatcher = null;
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            ReceiverInterceptorMatcher matcher)
        {
            Name = name;
            ReceiverTypePattern = receiverTypePattern;
            _symbolMatcher = null;
            _receiverMatcher = matcher;
        }

        public string Name { get; }

        public string ReceiverTypePattern { get; }

        public bool TryMatch(IMethodSymbol symbol, ITypeSymbol? receiverType, out InterceptorTarget target)
        {
            if (_receiverMatcher is not null)
                return _receiverMatcher(symbol, receiverType, out target);

            if (_symbolMatcher is not null)
                return _symbolMatcher(symbol, out target);

            target = default;
            return false;
        }
    }

    private readonly record struct ParameterSpec(string TypeName, string Name, string DefaultValueExpression = "", bool IsParams = false, RefKind RefKind = RefKind.None);

    private readonly record struct InterceptorTarget(
        InterceptorKind Kind,
        string ContractKey,
        string InstrumentationId,
        string ReceiverType,
        string MethodName,
        string ReturnType,
        EquatableArray<ParameterSpec> Parameters,
        bool IsAsync,
        string TypeParameterList = "",
        string ConstraintClauses = "",
        string ExtensionContainingType = "",
        EquatableArray<string> AdditionalContractKeys = default);

    private readonly record struct InterceptedInvocation(InterceptorTarget Target, InterceptableLocation Location);
}
