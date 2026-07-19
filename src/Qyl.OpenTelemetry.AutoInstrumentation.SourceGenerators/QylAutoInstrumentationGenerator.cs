using ANcpLua.Roslyn.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Diagnostics;
using Microsoft.CodeAnalysis.Text;

namespace Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators;

/// <summary>
/// Emits the qyl source-level auto-instrumentation interceptors used by NativeAOT consumers.
/// </summary>
/// <remarks>
/// The generator runs in the compiler, discovers source-visible invocation expressions, obtains
/// Roslyn <c>InterceptableLocation</c> data, and emits ordinary C# interceptor methods. Runtime
/// instrumentation stays in public qyl helper APIs; the generator never emits profiler, startup
/// hook, reflection, or runtime IL-rewrite code.
/// </remarks>
[Generator(LanguageNames.CSharp)]
public sealed partial class QylAutoInstrumentationGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat s_fullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    private delegate bool SymbolInterceptorMatcher(
        IMethodSymbol symbol,
        out InterceptorTarget target);

    private delegate bool ReceiverInterceptorMatcher(
        IMethodSymbol symbol,
        ITypeSymbol? receiverType,
        out InterceptorTarget target);

    private static readonly ImmutableArray<InterceptorMatcherDescriptor> s_matcherDescriptors =
        CreateGeneratedMatcherDescriptors();

    private static readonly ImmutableArray<InterceptorEmissionDescriptor> s_emissionDescriptors =
        CreateGeneratedEmissionDescriptors();

    // Runtime packages are excluded so QylIntercepted* forwarding helpers cannot self-intercept
    // (for example, QylInterceptedHttpClient.SendAsync calls client.SendAsync). Keep this set aligned
    // with runtime packages under /src; consumers, demos, and test fixtures remain instrumented.
    private static readonly HashSet<string> s_qylRuntimeAssemblies = new(StringComparer.Ordinal)
    {
        "Qyl.OpenTelemetry.AutoInstrumentation",
        "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners",
        "Qyl.OpenTelemetry.AutoInstrumentation.Hosting",
        "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore",
        "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient",
    };

    /// <summary>
    /// Registers the incremental syntax pipeline.
    /// </summary>
    /// <param name="context">Roslyn initialization context supplied by the compiler host.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        var interceptedInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (syntaxContext, cancellationToken) =>
                    TryCreateInterceptedInvocation(syntaxContext, cancellationToken))
            .Where(static invocation => invocation is not null)
            .Collect();

        context.RegisterSourceOutput(
            interceptedInvocations,
            static (productionContext, invocations) =>
                EmitInterceptors(productionContext, invocations));
    }

    private static InterceptedInvocation? TryCreateInterceptedInvocation(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (context.SemanticModel.Compilation.AssemblyName is { } assemblyName &&
            s_qylRuntimeAssemblies.Contains(assemblyName))
            return null;

        if (context.SemanticModel.GetInterceptorMethod(invocation, cancellationToken) is not null)
            return null;

        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol symbol)
            return null;

        var receiverType = GetInvocationReceiverType(invocation, context.SemanticModel, cancellationToken);
        if (!TryGetInvocation(symbol, receiverType, out var target))
            return null;

        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        if (interceptableLocation is null)
            return null;

        return new InterceptedInvocation(target, interceptableLocation);
    }

    private static ITypeSymbol? GetInvocationReceiverType(
        InvocationExpressionSyntax invocation,
        SemanticModel semanticModel,
        CancellationToken cancellationToken)
        => invocation.Expression is MemberAccessExpressionSyntax memberAccess
            ? semanticModel.GetTypeInfo(memberAccess.Expression, cancellationToken).Type
            : null;

    private static bool TryGetInvocation(IMethodSymbol symbol, ITypeSymbol? receiverType, out InterceptorTarget target)
    {
        foreach (var descriptor in s_matcherDescriptors)
        {
            if (descriptor.TryMatch(symbol, receiverType, out target))
                return true;
        }

        target = default;
        return false;
    }

    private static void EmitInterceptors(
        SourceProductionContext context,
        ImmutableArray<InterceptedInvocation?> nullableInvocations)
    {
        if (nullableInvocations.IsDefaultOrEmpty)
            return;

        var invocations = nullableInvocations
            .Where(static invocation => invocation is not null)
            .Select(static invocation => invocation!.Value)
            .Distinct()
            // Stable, content-based ordering so the emission order and the _N interceptor-name indices
            // are a pure function of the matched call sites — independent of Roslyn's cross-tree syntax
            // visitation order, which is NOT guaranteed stable across machines or incremental rebuilds.
            // Location.Data encodes file path + position and is unique per call site; the Target tie-break
            // is belt-and-suspenders. Keeps the generated file byte-reproducible (Directory.Build.props
            // sets Deterministic=true) once a consumer has 2+ matched call sites.
            .OrderBy(static invocation => invocation.Location.Data, StringComparer.Ordinal)
            .ThenBy(static invocation => invocation.Target.ReceiverType, StringComparer.Ordinal)
            .ThenBy(static invocation => invocation.Target.MethodName, StringComparer.Ordinal)
            .ToArray();

        if (invocations.Length is 0)
            return;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable");
        EmitInterceptsLocationAttribute(builder);
        builder.AppendLine("namespace Qyl.OpenTelemetry.AutoInstrumentation.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class QylGeneratedInterceptors");
        builder.AppendLine("    {");
        builder.AppendLine("        private const int RequiredQylGeneratedCodeAbi = global::Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode.QylGeneratedCodeAbi.V8;");
        builder.AppendLine();

        for (var index = 0; index < invocations.Length; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var invocation = invocations[index];
            var target = invocation.Target;
            var descriptor = GetEmissionDescriptor(in target);
            EmitInterceptorManifest(builder, in target);
            descriptor.Body.Emit(builder, in invocation, index);
        }

        builder.AppendLine("    }");
        var grpcStreamReaderHelperType = GetGrpcStreamReaderHelperType(invocations);
        if (!string.IsNullOrEmpty(grpcStreamReaderHelperType))
            EmitGrpcStreamReaderWrapper(builder, grpcStreamReaderHelperType);

        builder.AppendLine("}");

        context.AddSource("QylAutoInstrumentation.Interceptors.g.cs",
            SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static void EmitInterceptorManifest(StringBuilder builder, in InterceptorTarget target)
    {
        builder.Append("        // qyl-interceptor-manifest: {\"interceptorKind\":");
        AppendStringLiteral(builder, target.Kind.ToString());
        builder.Append(",\"signal\":");
        AppendStringLiteral(builder, GetManifestSignalName(target.Signal));
        builder.Append(",\"instrumentationId\":");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.Append(",\"additionalMetricIds\":[");
        for (var index = 0; index < target.AdditionalMetricIds.Length; index++)
        {
            if (index > 0)
                builder.Append(',');
            AppendStringLiteral(builder, target.AdditionalMetricIds[index]);
        }

        builder.Append("],\"contractKeys\":[");
        AppendStringLiteral(builder, GetContractKey(target.Signal, target.InstrumentationId));
        for (var index = 0; index < target.AdditionalMetricIds.Length; index++)
        {
            builder.Append(',');
            AppendStringLiteral(builder, GetContractKey(TelemetrySignal.Metrics, target.AdditionalMetricIds[index]));
        }

        builder.AppendLine("]}");
    }

    private static string GetContractKey(TelemetrySignal signal, string instrumentationId)
        => "signals." + GetManifestSignalName(signal) + "." + instrumentationId;

    private static string GetManifestSignalName(TelemetrySignal signal)
        => signal switch
        {
            TelemetrySignal.Traces => "traces",
            TelemetrySignal.Metrics => "metrics",
            TelemetrySignal.Logs => "logs",
            _ => throw new InvalidOperationException("Unknown telemetry signal: " + signal),
        };

    private static void EmitInterceptsLocationAttribute(StringBuilder builder)
    {
        builder.AppendLine("namespace System.Runtime.CompilerServices");
        builder.AppendLine("{");
        builder.AppendLine(
            "    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        builder.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        builder.AppendLine("    {");
        builder.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static InterceptorEmissionDescriptor GetEmissionDescriptor(in InterceptorTarget target)
    {
        foreach (var descriptor in s_emissionDescriptors)
        {
            if (descriptor.Kind == target.Kind)
                return descriptor;
        }

        throw new InvalidOperationException("Unsupported interceptor kind: " + target.Kind);
    }

    private static void EmitActivityDisposeFinally(StringBuilder builder)
    {
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
    }

    private static void EmitForwardingInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        ForwardingInterceptorBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        var receiverType = string.IsNullOrEmpty(descriptor.ReceiverTypeOverride)
            ? target.ReceiverType
            : descriptor.ReceiverTypeOverride;
        var helperMethodName = string.IsNullOrEmpty(descriptor.HelperMethodName)
            ? target.MethodName
            : descriptor.HelperMethodName;

        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            descriptor.MethodPrefix + "_" + target.MethodName,
            index,
            receiverType,
            descriptor.ReceiverName,
            target.Parameters,
            isAsync: false,
            target.TypeParameterList,
            target.ConstraintClauses);
        builder.Append("            => ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(helperMethodName);
        builder.Append('(');
        builder.Append(descriptor.ReceiverName);
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
        builder.AppendLine(");");
        builder.AppendLine();
    }

    private static void EmitTraceInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        TraceInterceptorBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        var runtimeObservesAsync = ShouldRuntimeObserveAsync(in target, descriptor);
        var signatureIsAsync = target.IsAsync && !runtimeObservesAsync;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            GetTraceMethodPrefix(in target, descriptor),
            index,
            target.ReceiverType,
            descriptor.ReceiverName,
            target.Parameters,
            signatureIsAsync,
            target.TypeParameterList,
            target.ConstraintClauses);
        builder.AppendLine("        {");
        if (descriptor.DurationMetric.IsDefined)
            descriptor.DurationMetric.AppendMetricStartStatement(builder);
        builder.Append("            var activity = ");
        descriptor.AppendStartActivity(builder, in target);
        builder.AppendLine(";");
        if (descriptor.ActivityEnrichment.IsDefined)
            descriptor.ActivityEnrichment.Append(builder, in target);
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        EmitTraceInvocation(builder, in target, descriptor);

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.AppendLine(descriptor.GetRecordExceptionStatement());
        if (descriptor.DurationMetric.IsDefined)
            descriptor.DurationMetric.AppendRecordDurationStatement(builder, in target);
        if (runtimeObservesAsync)
            builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        if (!runtimeObservesAsync)
            EmitActivityDisposeFinally(builder);
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static string GetTraceMethodPrefix(
        in InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
    {
        switch (descriptor.MethodPrefixKind)
        {
            case TraceMethodPrefixKind.Default:
                return descriptor.MethodPrefix + "_" + target.MethodName;
            case TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName:
                return target.InstrumentationId + "_" + target.MethodName;
            default:
                throw new InvalidOperationException("Unknown trace method prefix kind: " + descriptor.MethodPrefixKind);
        }
    }

    private static bool ShouldRuntimeObserveAsync(
        in InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
        => descriptor.AsyncObservation.IsDefined &&
           descriptor.AsyncObservation.AppliesTo(in target);

    private static void EmitTraceInvocation(
        StringBuilder builder,
        in InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
    {
        if (target.IsAsync && ShouldRuntimeObserveAsync(in target, descriptor))
        {
            builder.Append("                var resultTask = ");
            AppendInvocationCall(builder, in target, descriptor.ReceiverName);
            builder.AppendLine(";");
            builder.Append("                return ");
            builder.Append(descriptor.AsyncObservation.ObserveAsyncMethod);
            builder.AppendLine("(resultTask, activity);");
            return;
        }

        if (target.IsAsync)
        {
            if (IsTaskLikeReturnWithoutResult(target.ReturnType))
            {
                builder.Append("                await ");
                AppendInvocationCall(builder, in target, descriptor.ReceiverName);
                builder.AppendLine(".ConfigureAwait(false);");
                EmitTraceSuccessDurationMetric(builder, in target, descriptor);
                return;
            }

            builder.Append("                var result = await ");
            AppendInvocationCall(builder, in target, descriptor.ReceiverName);
            builder.AppendLine(".ConfigureAwait(false);");
            EmitTraceSuccessDurationMetric(builder, in target, descriptor);
            builder.AppendLine("                return result;");
            return;
        }

        if (string.Equals(target.ReturnType, "void", StringComparison.Ordinal))
        {
            builder.Append("                ");
            AppendInvocationCall(builder, in target, descriptor.ReceiverName);
            builder.AppendLine(";");
            EmitTraceSuccessDurationMetric(builder, in target, descriptor);
            return;
        }

        builder.Append("                var result = ");
        AppendInvocationCall(builder, in target, descriptor.ReceiverName);
        builder.AppendLine(";");
        EmitTraceSuccessDurationMetric(builder, in target, descriptor);
        builder.AppendLine("                return result;");
    }

    private static void EmitTraceSuccessDurationMetric(
        StringBuilder builder,
        in InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
    {
        if (descriptor.DurationMetric.IsDefined)
            descriptor.DurationMetric.AppendRecordDurationStatement(builder, in target);
    }

    private static bool IsTaskLikeReturnWithoutResult(string returnType)
        => returnType is "global::System.Threading.Tasks.Task" or
            "global::System.Threading.Tasks.ValueTask";

    private static void AppendTraceStartActivityArguments(
        StringBuilder builder,
        in InterceptorTarget target,
        TraceStartActivityArgumentKind argumentKind)
    {
        switch (argumentKind)
        {
            case TraceStartActivityArgumentKind.None:
                return;
            case TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName:
                AppendStringLiteral(builder, target.InstrumentationId);
                builder.Append(", ");
                AppendStringLiteral(builder, target.MethodName);
                return;
            case TraceStartActivityArgumentKind.ReceiverTypeAndTargetMethodName:
                AppendStringLiteral(builder, target.ReceiverType);
                builder.Append(", ");
                AppendStringLiteral(builder, target.MethodName);
                return;
            case TraceStartActivityArgumentKind.RedisOperationName:
                AppendStringLiteral(builder, GetRedisOperationName(target.MethodName));
                return;
            case TraceStartActivityArgumentKind.TargetMethodName:
                AppendStringLiteral(builder, target.MethodName);
                return;
            case TraceStartActivityArgumentKind.RabbitMqExchange:
                AppendRabbitMqExchangeExpression(builder, in target);
                return;
            default:
                throw new InvalidOperationException("Unknown trace start activity argument kind: " + argumentKind);
        }
    }

    private static void EmitDbCommandInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        DbCommandBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType,
            descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName,
            target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.Append("            var metricStart = ");
        builder.Append(descriptor.MetricsType);
        builder.Append('.');
        builder.Append(descriptor.GetTimestampMethod);
        builder.AppendLine("();");
        builder.Append("            const string instrumentationId = ");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.AppendLine(";");
        builder.Append("            var activity = ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.StartActivityMethod);
        builder.Append('(');
        builder.Append(descriptor.ReceiverName);
        builder.Append(", ");
        builder.Append("instrumentationId, ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var resultTask = ");
            builder.Append(descriptor.ReceiverName);
            builder.Append('.');
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.Append("                return ");
            builder.Append(descriptor.HelperType);
            builder.Append('.');
            builder.Append(descriptor.ObserveAsyncMethod);
            builder.Append("(resultTask, activity, metricStart, ");
            builder.Append("instrumentationId");
            builder.AppendLine(");");
        }
        else
        {
            builder.Append("                var result = ");
            builder.Append(descriptor.ReceiverName);
            builder.Append('.');
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");

            builder.Append("                ");
            builder.Append(descriptor.MetricsType);
            builder.Append('.');
            builder.Append(descriptor.RecordDurationMethod);
            builder.Append("(metricStart, ");
            builder.Append("instrumentationId");
            builder.AppendLine(");");
            builder.AppendLine("                return result;");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.RecordExceptionMethod);
        builder.AppendLine("(activity, exception);");
        builder.Append("                ");
        builder.Append(descriptor.MetricsType);
        builder.Append('.');
        builder.Append(descriptor.RecordDurationMethod);
        builder.Append("(metricStart, ");
        builder.Append("instrumentationId");
        builder.AppendLine(");");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        if (!target.IsAsync)
        {
            EmitActivityDisposeFinally(builder);
        }

        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        GrpcClientBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType,
            descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName,
            target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        EmitGrpcCallPreamble(builder, in target, descriptor);
        builder.Append("                return new ");
        builder.Append(target.ReturnType);
        builder.AppendLine("(");
        EmitGrpcConstructorArguments(builder, descriptor);
        EmitGrpcDisposeAction(builder, descriptor);
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.Append(descriptor.HelperType);
        builder.AppendLine(".RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcConstructorArguments(StringBuilder builder, GrpcClientBodyDescriptor descriptor)
    {
        switch (descriptor.Shape)
        {
            case GrpcClientCallShape.Unary:
                builder.Append("                    ");
                builder.Append(descriptor.HelperType);
                builder.AppendLine(
                    ".ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
                break;

            case GrpcClientCallShape.ServerStreaming:
                builder.AppendLine(
                    "                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
                break;

            case GrpcClientCallShape.ClientStreaming:
                builder.AppendLine("                    call.RequestStream,");
                builder.Append("                    ");
                builder.Append(descriptor.HelperType);
                builder.AppendLine(
                    ".ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
                break;

            case GrpcClientCallShape.DuplexStreaming:
                builder.AppendLine("                    call.RequestStream,");
                builder.AppendLine(
                    "                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
                break;

            default:
                throw new InvalidOperationException("Unknown gRPC client call shape: " + descriptor.Shape);
        }

        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
    }

    private static void EmitGrpcCallPreamble(StringBuilder builder, in InterceptorTarget target,
        GrpcClientBodyDescriptor descriptor)
    {
        builder.Append("            var activity = ");
        builder.Append(descriptor.HelperType);
        builder.Append(".StartActivity(");
        AppendStringLiteral(builder, target.ReceiverType);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.Append(", ");
        AppendGrpcMetadataExpression(builder, in target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var call = ");
        builder.Append(descriptor.ReceiverName);
        builder.Append('.');
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.Append("                var observedResponseHeaders = ");
        builder.Append(descriptor.HelperType);
        builder.AppendLine(".ObserveResponseHeadersAsync(call.ResponseHeadersAsync, activity);");
    }

    private static void EmitGrpcDisposeAction(StringBuilder builder, GrpcClientBodyDescriptor descriptor)
    {
        builder.AppendLine("                    () =>");
        builder.AppendLine("                    {");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            call.Dispose();");
        builder.AppendLine("                        }");
        builder.AppendLine("                        finally");
        builder.AppendLine("                        {");
        builder.Append("                            ");
        builder.Append(descriptor.HelperType);
        builder.AppendLine(".Dispose(activity);");
        builder.AppendLine("                        }");
        builder.AppendLine("                    });");
    }

    private static void AppendGrpcMetadataExpression(StringBuilder builder, in InterceptorTarget target)
    {
        foreach (var parameter in target.Parameters)
        {
            if (string.Equals(parameter.TypeName, "global::Grpc.Core.Metadata", StringComparison.Ordinal))
            {
                builder.Append(parameter.Name);
                return;
            }
        }

        builder.Append("null");
    }

    private static void EmitGrpcStreamReaderWrapper(StringBuilder builder, string helperType)
    {
        builder.AppendLine();
        builder.AppendLine("    internal static class QylObservedAsyncStreamReader");
        builder.AppendLine("    {");
        builder.AppendLine(
            "        public static global::Grpc.Core.IAsyncStreamReader<T> Create<T>(global::Grpc.Core.IAsyncStreamReader<T> inner, global::System.Diagnostics.Activity? activity, global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? responseHeadersTask)");
        builder.AppendLine("            => new QylObservedAsyncStreamReader<T>(inner, activity, responseHeadersTask);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine(
            "    internal sealed class QylObservedAsyncStreamReader<T> : global::Grpc.Core.IAsyncStreamReader<T>");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::Grpc.Core.IAsyncStreamReader<T> _inner;");
        builder.AppendLine("        private readonly global::System.Diagnostics.Activity? _activity;");
        builder.AppendLine(
            "        private readonly global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? _responseHeadersTask;");
        builder.AppendLine("        private bool _completed;");
        builder.AppendLine();
        builder.AppendLine(
            "        public QylObservedAsyncStreamReader(global::Grpc.Core.IAsyncStreamReader<T> inner, global::System.Diagnostics.Activity? activity, global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? responseHeadersTask)");
        builder.AppendLine("        {");
        builder.AppendLine("            _inner = inner;");
        builder.AppendLine("            _activity = activity;");
        builder.AppendLine("            _responseHeadersTask = responseHeadersTask;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public T Current => _inner.Current;");
        builder.AppendLine();
        builder.AppendLine(
            "        public async global::System.Threading.Tasks.Task<bool> MoveNext(global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("        {");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine(
            "                var hasNext = await _inner.MoveNext(cancellationToken).ConfigureAwait(false);");
        builder.AppendLine("                if (!hasNext && !_completed)");
        builder.AppendLine("                {");
        builder.AppendLine("                    _completed = true;");
        builder.Append("                    ");
        builder.Append(helperType);
        builder.AppendLine(".CaptureCompletedResponseHeaders(_responseHeadersTask, _activity);");
        builder.Append("                    ");
        builder.Append(helperType);
        builder.AppendLine(".RecordStreamingComplete(_activity);");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return hasNext;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.Append(helperType);
        builder.AppendLine(".RecordException(_activity, exception);");
        builder.Append("                ");
        builder.Append(helperType);
        builder.AppendLine(".Dispose(_activity);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void AppendGraphQlDocumentCaptureExpression(StringBuilder builder, in InterceptorTarget target)
    {
        if (target.Parameters.Length > 0 && string.Equals(target.Parameters[0].TypeName,
                "global::GraphQL.ExecutionOptions", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            builder.Append(" is not null ? ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(".Query");
            builder.Append(" : null");
            return;
        }

        builder.Append("null");
    }

    private static void AppendGraphQlOperationNameExpression(StringBuilder builder, in InterceptorTarget target)
    {
        if (target.Parameters.Length > 0 && string.Equals(target.Parameters[0].TypeName,
                "global::GraphQL.ExecutionOptions", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            builder.Append(" is not null ? ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(".OperationName : null");
            return;
        }

        builder.Append("null");
    }

    private static void AppendRabbitMqExchangeExpression(StringBuilder builder, in InterceptorTarget target)
    {
        if (target.Parameters.Length > 0 &&
            string.Equals(target.Parameters[0].TypeName, "string", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            return;
        }

        builder.Append("null");
    }

    private static void EmitLoggerInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        if (descriptor.Kind is LoggerInterceptorBodyKind.ILoggerLog)
        {
            EmitDirectLoggerInterceptor(builder, in invocation, index, descriptor);
            return;
        }

        EmitLoggerExtensionInterceptor(builder, in invocation, index, descriptor);
    }

    private static void EmitDirectLoggerInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        var attribute = invocation.Location.GetInterceptsLocationAttributeSyntax();
        var displayLocation = invocation.Location.GetDisplayLocation();
        builder.Append("        // Intercepted call at ");
        builder.AppendLine(displayLocation);
        builder.Append("        ");
        builder.AppendLine(attribute);
        builder.Append("        public static void ");
        builder.Append(descriptor.MethodPrefix);
        builder.Append('_');
        builder.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine("<TState>(");
        builder.AppendLine("            this global::Microsoft.Extensions.Logging.ILogger logger,");
        builder.AppendLine("            global::Microsoft.Extensions.Logging.LogLevel logLevel,");
        builder.AppendLine("            global::Microsoft.Extensions.Logging.EventId eventId,");
        builder.AppendLine("            TState state,");
        builder.AppendLine("            global::System.Exception? exception,");
        builder.AppendLine("            global::System.Func<TState, global::System.Exception?, string> formatter)");
        builder.Append("            => ");
        builder.Append(descriptor.HelperType);
        builder.AppendLine(".Log(logger, logLevel, eventId, state, exception, formatter);");
        builder.AppendLine();
    }

    private static void EmitLoggerExtensionInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, "void",
            descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, "logger", target.Parameters,
            isAsync: false);
        builder.Append("            => ");
        builder.Append(descriptor.HelperType);
        builder.Append(".LogExtension(logger, ");
        AppendLoggerLevelExpression(builder, in target);
        builder.Append(", ");
        AppendFirstParameterExpression(builder, in target, "global::Microsoft.Extensions.Logging.EventId", "default");
        builder.Append(", ");
        AppendFirstParameterExpression(builder, in target, "global::System.Exception", "null");
        builder.Append(", ");
        AppendFirstParameterExpression(builder, in target, "global::System.String", "null");
        builder.Append(", ");
        AppendFirstArrayParameterExpression(builder, in target, "global::System.Object",
            "global::System.Array.Empty<object>()");
        builder.AppendLine(");");
        builder.AppendLine();
    }

    private static void EmitExternalLoggerInterceptor(
        StringBuilder builder,
        in InterceptedInvocation invocation,
        int index,
        ExternalLoggerBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            "void",
            target.InstrumentationId + "_" + target.MethodName,
            index,
            target.ReceiverType,
            "logger",
            target.Parameters,
            isAsync: false,
            typeParameterList: target.TypeParameterList,
            constraintClauses: target.ConstraintClauses);
        builder.AppendLine("        {");
        if (target.ExtensionContainingType is { Length: > 0 })
        {
            builder.Append("            if (!logger.");
            builder.Append(target.ExtensionContainingType);
            builder.AppendLine(")");
            builder.AppendLine("            {");
            builder.Append("                logger.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.AppendLine("                return;");
            builder.AppendLine("            }");
        }

        builder.Append("            var activity = ");
        builder.Append(descriptor.HelperType);
        builder.Append(".StartActivity(");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.Append(", ");
        builder.Append(descriptor.DomainExpression);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.Append(", ");
        AppendExternalLoggerSeverityExpression(builder, in target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                logger.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.Append(descriptor.HelperType);
        builder.AppendLine(".RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        EmitActivityDisposeFinally(builder);
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void AppendExternalLoggerSeverityExpression(StringBuilder builder, in InterceptorTarget target)
    {
        foreach (var parameter in target.Parameters)
        {
            if (string.Equals(parameter.TypeName, "global::NLog.LogLevel", StringComparison.Ordinal) ||
                string.Equals(parameter.TypeName, "global::log4net.Core.Level", StringComparison.Ordinal))
            {
                builder.Append(parameter.Name);
                builder.Append(" is null ? null : ");
                builder.Append(parameter.Name);
                builder.Append(".Name");
                return;
            }

            if (string.Equals(parameter.TypeName, "global::NLog.LogEventInfo", StringComparison.Ordinal) ||
                string.Equals(parameter.TypeName, "global::log4net.Core.LoggingEvent", StringComparison.Ordinal))
            {
                builder.Append(parameter.Name);
                builder.Append(" is null ? null : ");
                builder.Append(parameter.Name);
                builder.Append(".Level is null ? null : ");
                builder.Append(parameter.Name);
                builder.Append(".Level.Name");
                return;
            }
        }

        builder.Append("null");
    }

    private static void AppendLoggerLevelExpression(StringBuilder builder, in InterceptorTarget target)
    {
        if (string.Equals(target.MethodName, "Log", StringComparison.Ordinal))
        {
            AppendFirstParameterExpression(builder, in target, "global::Microsoft.Extensions.Logging.LogLevel",
                "global::Microsoft.Extensions.Logging.LogLevel.None");
            return;
        }

        var levelName = GetLoggerExtensionLevelName(target.MethodName)
            ?? throw new InvalidOperationException("Unknown logger extension method: " + target.MethodName);
        builder.Append("global::Microsoft.Extensions.Logging.LogLevel.");
        builder.Append(levelName);
    }

    private static void AppendFirstParameterExpression(StringBuilder builder, in InterceptorTarget target,
        string typeName, string fallbackExpression)
    {
        foreach (var parameter in target.Parameters)
        {
            if (IsParameterType(parameter, typeName))
            {
                builder.Append(parameter.Name);
                return;
            }
        }

        builder.Append(fallbackExpression);
    }

    private static void AppendFirstArrayParameterExpression(StringBuilder builder, in InterceptorTarget target,
        string elementTypeName, string fallbackExpression)
    {
        foreach (var parameter in target.Parameters)
        {
            if (IsParameterType(parameter, elementTypeName + "[]"))
            {
                builder.Append(parameter.Name);
                return;
            }
        }

        builder.Append(fallbackExpression);
    }

    private static bool IsParameterType(ParameterSpec parameter, string typeName)
        => string.Equals(NormalizeSpecialTypeName(parameter.TypeName), NormalizeSpecialTypeName(typeName),
            StringComparison.Ordinal);

    private static string NormalizeSpecialTypeName(string typeName)
        => typeName switch
        {
            "string" => "global::System.String",
            "string[]" => "global::System.String[]",
            "object" => "global::System.Object",
            "object[]" => "global::System.Object[]",
            _ => typeName,
        };

    private static void EmitAttributeAndSignature(
        StringBuilder builder,
        InterceptableLocation location,
        string returnType,
        string methodPrefix,
        int index,
        string receiverType,
        string receiverName,
        EquatableArray<ParameterSpec> parameters,
        bool isAsync)
        => EmitAttributeAndSignature(builder, location, returnType, methodPrefix, index, receiverType, receiverName,
            parameters, isAsync, string.Empty, string.Empty);

    private static void EmitAttributeAndSignature(
        StringBuilder builder,
        InterceptableLocation location,
        string returnType,
        string methodPrefix,
        int index,
        string receiverType,
        string receiverName,
        EquatableArray<ParameterSpec> parameters,
        bool isAsync,
        string typeParameterList,
        string constraintClauses)
    {
        var attribute = location.GetInterceptsLocationAttributeSyntax();
        var displayLocation = location.GetDisplayLocation();
        builder.Append("        // Intercepted call at ");
        builder.AppendLine(displayLocation);
        builder.Append("        ");
        builder.AppendLine(attribute);
        builder.Append("        public static ");
        if (isAsync)
            builder.Append("async ");

        builder.Append(returnType);
        builder.Append(' ');
        builder.Append(methodPrefix);
        builder.Append('_');
        builder.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.Append(typeParameterList);
        builder.Append("(this ");
        builder.Append(receiverType);
        builder.Append(' ');
        builder.Append(receiverName);
        AppendParameterList(builder, parameters);
        builder.Append(')');
        builder.Append(constraintClauses);
        builder.AppendLine();
    }

    private static void AppendGenericTypeArgumentList(StringBuilder builder, string typeParameterList)
        => builder.Append(typeParameterList);

    private static void AppendParameterList(StringBuilder builder, EquatableArray<ParameterSpec> parameters)
    {
        foreach (var parameter in parameters)
        {
            builder.Append(", ");
            if (parameter.IsParams)
                builder.Append("params ");

            if (parameter.RefKind is RefKind.In)
                builder.Append("in ");

            builder.Append(parameter.TypeName);
            builder.Append(' ');
            builder.Append(parameter.Name);
            if (!string.IsNullOrEmpty(parameter.DefaultValueExpression))
            {
                builder.Append(" = ");
                builder.Append(parameter.DefaultValueExpression);
            }
        }
    }

    private static void AppendArgumentList(StringBuilder builder, EquatableArray<ParameterSpec> parameters,
        bool includeLeadingComma)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0 || includeLeadingComma)
                builder.Append(", ");

            if (parameters[i].RefKind is RefKind.In)
                builder.Append("in ");

            builder.Append(parameters[i].Name);
        }
    }

    private static bool HasByRefParameters(EquatableArray<ParameterSpec> parameters)
    {
        foreach (var parameter in parameters)
        {
            if (parameter.RefKind is not RefKind.None)
                return true;
        }

        return false;
    }

    private static void AppendInvocationCall(StringBuilder builder, in InterceptorTarget target, string receiverName)
    {
        if (!string.IsNullOrEmpty(target.ExtensionContainingType))
        {
            builder.Append(target.ExtensionContainingType);
            builder.Append('.');
            builder.Append(target.MethodName);
            AppendGenericTypeArgumentList(builder, target.TypeParameterList);
            builder.Append('(');
            builder.Append(receiverName);
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
            builder.Append(')');
            return;
        }

        builder.Append(receiverName);
        builder.Append('.');
        builder.Append(target.MethodName);
        AppendGenericTypeArgumentList(builder, target.TypeParameterList);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.Append(')');
    }
}
