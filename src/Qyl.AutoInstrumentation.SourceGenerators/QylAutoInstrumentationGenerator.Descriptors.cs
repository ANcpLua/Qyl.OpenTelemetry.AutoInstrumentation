using ANcpLua.Roslyn.Utilities;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Qyl.AutoInstrumentation.SourceGenerators;

public sealed partial class QylAutoInstrumentationGenerator
{
    private enum InterceptorKind
    {
        HttpClient,
        HttpWebRequest,
        AspNetCoreWebApplicationBuilderBuild,
        AspNetCoreRequestDelegate,
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

    private enum InterceptorEmitterFamily
    {
        AspNetCore,
        Azure,
        Cache,
        Database,
        GraphQl,
        Grpc,
        HttpClient,
        Logging,
        Messaging,
        Meter,
        Scheduler,
        Search,
        Wcf,
    }

    private enum InterceptorMethodShape
    {
        AsyncOrSyncValue,
        AsyncOrSyncVoid,
        AsyncTask,
        AsyncValue,
        BuilderInitialization,
        BuilderRegistration,
        EndpointRegistration,
        GrpcStreaming,
        GrpcUnary,
        SyncValue,
        Void,
    }

    private enum InterceptorSignalOwnership
    {
        Trace,
        Metric,
        Log,
        TraceAndMetric,
    }

    private enum InterceptorErrorPolicy
    {
        None,
        Exception,
        GrpcStatusAndException,
        HttpStatusAndException,
        RuntimeDelegate,
    }

    private enum InterceptorDurationPolicy
    {
        None,
        RuntimeMetric,
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
        InterceptorEmitterFamily Family,
        InterceptorMethodShape MethodShape,
        InterceptorSignalOwnership SignalOwnership,
        InterceptorErrorPolicy ErrorPolicy,
        InterceptorDurationPolicy DurationPolicy,
        TraceInterceptorBodyDescriptor TraceBody = default,
        ForwardingInterceptorBodyDescriptor ForwardingBody = default,
        HttpWebRequestBodyDescriptor HttpWebRequestBody = default,
        DbCommandBodyDescriptor DbCommandBody = default,
        GrpcClientBodyDescriptor GrpcClientBody = default,
        MeterProviderBuilderBodyDescriptor MeterProviderBuilderBody = default,
        LoggerBodyDescriptor LoggerBody = default,
        ExternalLoggerBodyDescriptor ExternalLoggerBody = default);

    private readonly record struct GrpcClientBodyDescriptor(
        GrpcClientCallShape Shape,
        string MethodPrefix,
        string ReceiverName,
        string HelperType,
        bool IsDefined = true);

    private readonly record struct DbCommandBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string HelperType,
        string MetricsType,
        string GetTimestampMethod,
        string StartActivityMethod,
        string ObserveAsyncMethod,
        string RecordExceptionMethod,
        string RecordDurationMethod,
        bool IsDefined = true);

    private readonly record struct HttpWebRequestBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string RequestType,
        string HelperType,
        string GetStartTimeUtcMethod,
        string StartActivityMethod,
        string RecordResultMethod,
        string RecordExceptionMethod,
        bool IsDefined = true);

    private readonly record struct MeterProviderBuilderBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string EnabledMeterNamesExpression,
        bool IsDefined = true);

    private readonly record struct LoggerBodyDescriptor(
        LoggerInterceptorBodyKind Kind,
        string MethodPrefix,
        string HelperType,
        bool IsDefined = true);

    private readonly record struct ExternalLoggerBodyDescriptor(
        string HelperType,
        string DomainExpression,
        bool IsDefined = true);

    private readonly record struct ForwardingInterceptorBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        string HelperType,
        string HelperMethodName = "",
        string ReceiverTypeOverride = "",
        bool IsDefined = true);

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

        public void AppendRecordDurationStatement(StringBuilder builder, InterceptorTarget target)
        {
            builder.Append("                ");
            builder.Append(HelperType);
            builder.Append('.');
            builder.Append(RecordDurationMethod);
            builder.Append("(metricStart");
            AppendRecordDurationArguments(builder, target);
            builder.AppendLine(");");
        }

        private void AppendRecordDurationArguments(StringBuilder builder, InterceptorTarget target)
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
        public void Append(StringBuilder builder, InterceptorTarget target)
        {
            builder.AppendLine("            if (activity is not null)");
            builder.AppendLine("            {");
            builder.Append("                ");
            builder.Append(HelperType);
            builder.Append('.');
            builder.Append(EnrichMethod);
            builder.Append("(activity");
            AppendArguments(builder, target);
            builder.AppendLine(");");
            builder.AppendLine("            }");
        }

        private void AppendArguments(StringBuilder builder, InterceptorTarget target)
        {
            switch (Arguments)
            {
                case TraceActivityEnrichmentArgumentKind.None:
                    return;
                case TraceActivityEnrichmentArgumentKind.GraphQlExecutionOptions:
                    builder.Append(", ");
                    QylAutoInstrumentationGenerator.AppendGraphQlOperationNameExpression(builder, target);
                    builder.Append(", ");
                    QylAutoInstrumentationGenerator.AppendGraphQlDocumentCaptureExpression(builder, target);
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
        public bool AppliesTo(InterceptorTarget target)
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

    private readonly record struct TraceInterceptorBodyDescriptor(
        string MethodPrefix,
        string ReceiverName,
        TraceMethodPrefixKind MethodPrefixKind = TraceMethodPrefixKind.Default,
        bool IsDefined = true,
        TraceRuntimeHelperDescriptor RuntimeHelper = default,
        TraceDurationMetricDescriptor DurationMetric = default,
        TraceActivityEnrichmentDescriptor ActivityEnrichment = default,
        TraceAsyncObservationDescriptor AsyncObservation = default)
    {
        public void AppendStartActivity(StringBuilder builder, InterceptorTarget target)
        {
            builder.Append(RuntimeHelper.HelperType);
            builder.Append('.');
            builder.Append(RuntimeHelper.StartActivityMethod);
            builder.Append('(');
            QylAutoInstrumentationGenerator.AppendTraceStartActivityArguments(builder, target, RuntimeHelper.StartActivityArguments);
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
            InterceptorKind targetKind,
            string contractKey,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            SymbolInterceptorMatcher matcher)
            : this(name, receiverTypePattern, QylAutoInstrumentationGenerator.InterceptorKinds(targetKind), QylAutoInstrumentationGenerator.ContractKeys(contractKey), family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            InterceptorKind targetKind,
            EquatableArray<string> contractKeys,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            SymbolInterceptorMatcher matcher)
            : this(name, receiverTypePattern, QylAutoInstrumentationGenerator.InterceptorKinds(targetKind), contractKeys, family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            ulong targetKindMask,
            string contractKey,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            SymbolInterceptorMatcher matcher)
            : this(name, receiverTypePattern, targetKindMask, QylAutoInstrumentationGenerator.ContractKeys(contractKey), family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            ulong targetKindMask,
            EquatableArray<string> contractKeys,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            SymbolInterceptorMatcher matcher)
        {
            Name = name;
            ReceiverTypePattern = receiverTypePattern;
            TargetKindMask = targetKindMask is 0
                ? throw new InvalidOperationException("Matcher descriptor '" + name + "' must declare at least one interceptor kind.")
                : targetKindMask;
            ContractKeys = contractKeys;
            Family = family;
            MethodShape = methodShape;
            _symbolMatcher = matcher;
            _receiverMatcher = null;
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            InterceptorKind targetKind,
            string contractKey,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            ReceiverInterceptorMatcher matcher)
            : this(name, receiverTypePattern, QylAutoInstrumentationGenerator.InterceptorKinds(targetKind), QylAutoInstrumentationGenerator.ContractKeys(contractKey), family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            InterceptorKind targetKind,
            EquatableArray<string> contractKeys,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            ReceiverInterceptorMatcher matcher)
            : this(name, receiverTypePattern, QylAutoInstrumentationGenerator.InterceptorKinds(targetKind), contractKeys, family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            ulong targetKindMask,
            string contractKey,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            ReceiverInterceptorMatcher matcher)
            : this(name, receiverTypePattern, targetKindMask, QylAutoInstrumentationGenerator.ContractKeys(contractKey), family, methodShape, matcher)
        {
        }

        public InterceptorMatcherDescriptor(
            string name,
            string receiverTypePattern,
            ulong targetKindMask,
            EquatableArray<string> contractKeys,
            InterceptorEmitterFamily family,
            InterceptorMethodShape methodShape,
            ReceiverInterceptorMatcher matcher)
        {
            Name = name;
            ReceiverTypePattern = receiverTypePattern;
            TargetKindMask = targetKindMask is 0
                ? throw new InvalidOperationException("Matcher descriptor '" + name + "' must declare at least one interceptor kind.")
                : targetKindMask;
            ContractKeys = contractKeys;
            Family = family;
            MethodShape = methodShape;
            _symbolMatcher = null;
            _receiverMatcher = matcher;
        }

        public string Name { get; }

        public string ReceiverTypePattern { get; }

        public ulong TargetKindMask { get; }

        public EquatableArray<string> ContractKeys { get; }

        public InterceptorEmitterFamily Family { get; }

        public InterceptorMethodShape MethodShape { get; }

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
        EquatableArray<string> AdditionalContractKeys = default,
        string MatcherName = "",
        string MatcherReceiverTypePattern = "",
        InterceptorEmitterFamily MatcherFamily = default,
        InterceptorMethodShape MatcherMethodShape = default,
        EquatableArray<string> MatcherContractKeys = default);

    private readonly record struct InterceptedInvocation(InterceptorTarget Target, InterceptableLocation Location);
}
