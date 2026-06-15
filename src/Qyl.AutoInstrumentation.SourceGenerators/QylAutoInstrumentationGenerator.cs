using ANcpLua.Roslyn.Utilities;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Qyl.AutoInstrumentation.SourceGenerators;

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

    // The qyl runtime packages hold the QylIntercepted* forwarding helpers, whose own framework
    // calls would self-intercept (e.g. QylInterceptedHttpClient.SendAsync calls client.SendAsync).
    // A compilation that IS one of these packages is never instrumented. Today only the core package
    // carries self-forwarding helpers and only it gets the analyzer, but matching the whole set keeps
    // the guard correct under the documented package-boundary moves (a helper relocating to a sibling
    // that later gains the analyzer). Keep in sync with the runtime packages under /src — a consumer,
    // demo, or test fixture is deliberately NOT in this set and stays instrumented.
    private static readonly HashSet<string> s_qylRuntimeAssemblies = new(StringComparer.Ordinal)
    {
        "Qyl.AutoInstrumentation",
        "Qyl.AutoInstrumentation.DiagnosticListeners",
        "Qyl.AutoInstrumentation.Hosting",
        "Qyl.AutoInstrumentation.EntityFrameworkCore",
        "Qyl.AutoInstrumentation.SqlClient",
    };

    /// <summary>
    /// Registers the incremental syntax pipeline and post-initialization contract manifest output.
    /// </summary>
    /// <param name="context">Roslyn initialization context supplied by the compiler host.</param>
    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
        ValidateDescriptorCatalog();

        context.RegisterPostInitializationOutput(static output =>
        {
            output.AddSource(
                "QylGeneratedInstrumentationContract.g.cs",
                SourceText.From(InstrumentationContract.EmitGeneratedManifestSource(), Encoding.UTF8));
        });

        var interceptedInvocations = context.SyntaxProvider
            .CreateSyntaxProvider(
                static (node, _) => node is InvocationExpressionSyntax,
                static (syntaxContext, cancellationToken) => TryCreateInterceptedInvocation(syntaxContext, cancellationToken))
            .Where(static invocation => invocation is not null)
            .Collect();

        context.RegisterSourceOutput(interceptedInvocations, EmitInterceptors);
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

        if (!IsSupportedTarget(target))
            return null;

        var interceptableLocation = context.SemanticModel.GetInterceptableLocation(invocation, cancellationToken);
        if (interceptableLocation is null)
            return null;

        return new InterceptedInvocation(target, interceptableLocation);
    }

    private static bool IsSupportedTarget(InterceptorTarget target)
    {
        if (InstrumentationContract.TryGetImplementedSignal(target.ContractKey) is null)
            return false;

        if (target.AdditionalContractKeys is not { Length: > 0 } additionalContractKeys)
            return true;

        foreach (var contractKey in additionalContractKeys)
        {
            if (InstrumentationContract.TryGetImplementedSignal(contractKey) is null)
                return false;
        }

        return true;
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
            {
                EnsureTargetDeclaredByMatcher(descriptor, target);
                target = target with
                {
                    MatcherName = descriptor.Name,
                    MatcherReceiverTypePattern = descriptor.ReceiverTypePattern,
                    MatcherFamily = descriptor.Family,
                    MatcherMethodShape = descriptor.MethodShape,
                    MatcherContractKeys = descriptor.ContractKeys,
                };
                return true;
            }
        }

        target = default;
        return false;
    }

    private static void EnsureTargetDeclaredByMatcher(InterceptorMatcherDescriptor descriptor, InterceptorTarget target)
    {
        EnsureKindDeclaredByMatcher(descriptor, target.Kind);
        EnsureContractDeclaredByMatcher(descriptor, target.ContractKey, target.Kind);

        if (target.AdditionalContractKeys is not { Length: > 0 } additionalContractKeys)
            return;

        foreach (var contractKey in additionalContractKeys)
            EnsureContractDeclaredByMatcher(descriptor, contractKey, target.Kind);
    }

    private static void EnsureKindDeclaredByMatcher(InterceptorMatcherDescriptor descriptor, InterceptorKind kind)
    {
        if ((descriptor.TargetKindMask & GetInterceptorKindMask(kind)) is not 0)
            return;

        throw new InvalidOperationException(
            "Matcher descriptor '" + descriptor.Name + "' produced undeclared interceptor kind '" + kind + "'.");
    }

    private static void EnsureContractDeclaredByMatcher(
        InterceptorMatcherDescriptor descriptor,
        string contractKey,
        InterceptorKind kind)
    {
        foreach (var declaredContractKey in descriptor.ContractKeys)
        {
            if (string.Equals(declaredContractKey, contractKey, StringComparison.Ordinal))
                return;
        }

        throw new InvalidOperationException(
            "Matcher descriptor '" + descriptor.Name + "' produced interceptor kind '" + kind + "' for undeclared contract key '" + contractKey + "'.");
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
        builder.AppendLine("namespace Qyl.AutoInstrumentation.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class QylGeneratedInterceptors");
        builder.AppendLine("    {");

        for (var index = 0; index < invocations.Length; index++)
        {
            context.CancellationToken.ThrowIfCancellationRequested();
            var invocation = invocations[index];
            var descriptor = GetEmissionDescriptor(invocation.Target);
            if (descriptor.TraceBody.IsDefined)
            {
                EmitTraceInterceptor(builder, invocation, index, descriptor.TraceBody);
                continue;
            }

            if (descriptor.ForwardingBody.IsDefined)
            {
                EmitForwardingInterceptor(builder, invocation, index, descriptor.ForwardingBody);
                continue;
            }

            if (descriptor.HttpWebRequestBody.IsDefined)
            {
                EmitHttpWebRequestInterceptor(builder, invocation, index, descriptor.HttpWebRequestBody);
                continue;
            }

            if (descriptor.DbCommandBody.IsDefined)
            {
                EmitDbCommandInterceptor(builder, invocation, index, descriptor.DbCommandBody);
                continue;
            }

            if (descriptor.GrpcClientBody.IsDefined)
            {
                EmitGrpcNetClientInterceptor(builder, invocation, index, descriptor.GrpcClientBody);
                continue;
            }

            if (descriptor.MeterProviderBuilderBody.IsDefined)
            {
                EmitMeterProviderBuilderAddMeterInterceptor(builder, invocation, index, descriptor.MeterProviderBuilderBody);
                continue;
            }

            if (descriptor.LoggerBody.IsDefined)
            {
                EmitLoggerInterceptor(builder, invocation, index, descriptor.LoggerBody);
                continue;
            }

            if (descriptor.ExternalLoggerBody.IsDefined)
            {
                EmitExternalLoggerInterceptor(builder, invocation, index, descriptor.ExternalLoggerBody);
                continue;
            }

            throw new InvalidOperationException("Interceptor emission descriptor has no body: " + descriptor.Kind);
        }

        builder.AppendLine("    }");
        var grpcStreamReaderHelperType = GetGrpcStreamReaderHelperType(invocations);
        if (!string.IsNullOrEmpty(grpcStreamReaderHelperType))
            EmitGrpcStreamReaderWrapper(builder, grpcStreamReaderHelperType);

        builder.AppendLine("}");

        context.AddSource("QylAutoInstrumentation.Interceptors.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static void EmitInterceptsLocationAttribute(StringBuilder builder)
    {
        builder.AppendLine("namespace System.Runtime.CompilerServices");
        builder.AppendLine("{");
        builder.AppendLine("    [global::System.AttributeUsage(global::System.AttributeTargets.Method, AllowMultiple = true)]");
        builder.AppendLine("    file sealed class InterceptsLocationAttribute : global::System.Attribute");
        builder.AppendLine("    {");
        builder.AppendLine("        public InterceptsLocationAttribute(int version, string data)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static InterceptorEmissionDescriptor GetEmissionDescriptor(InterceptorTarget target)
    {
        foreach (var descriptor in s_emissionDescriptors)
        {
            if (descriptor.Kind != target.Kind)
                continue;

            EnsureEmissionDescriptorMatchesMatcher(target, descriptor);
            ValidateEmissionDescriptorPolicy(descriptor);
            return descriptor;
        }

        throw new InvalidOperationException("Unsupported interceptor kind: " + target.Kind);
    }

    private static void ValidateDescriptorCatalog()
    {
        var matcherKindMask = 0UL;
        foreach (var descriptor in s_matcherDescriptors)
        {
            if ((matcherKindMask & descriptor.TargetKindMask) is not 0)
                throw new InvalidOperationException("Matcher descriptor catalog declares a duplicate interceptor kind: " + descriptor.Name);

            matcherKindMask |= descriptor.TargetKindMask;
        }

        var emissionKindMask = 0UL;
        foreach (var descriptor in s_emissionDescriptors)
        {
            var kindMask = GetInterceptorKindMask(descriptor.Kind);
            if ((emissionKindMask & kindMask) is not 0)
                throw new InvalidOperationException("Emission descriptor catalog declares a duplicate interceptor kind: " + descriptor.Kind);

            emissionKindMask |= kindMask;
            ValidateEmissionDescriptorPolicy(descriptor);
        }

        if (matcherKindMask != emissionKindMask)
            throw new InvalidOperationException("Matcher and emission descriptor catalogs must declare the same interceptor kind set.");
    }

    private static void EnsureEmissionDescriptorMatchesMatcher(
        InterceptorTarget target,
        InterceptorEmissionDescriptor descriptor)
    {
        if (string.IsNullOrEmpty(target.MatcherName))
            throw new InvalidOperationException("Interceptor kind '" + target.Kind + "' has no matcher descriptor.");

        if (target.MatcherFamily != descriptor.Family)
        {
            throw new InvalidOperationException(
                "Matcher descriptor '" + target.MatcherName + "' and emission descriptor '" + descriptor.Kind + "' disagree on emitter family.");
        }

        if (target.MatcherMethodShape != descriptor.MethodShape)
        {
            throw new InvalidOperationException(
                "Matcher descriptor '" + target.MatcherName + "' and emission descriptor '" + descriptor.Kind + "' disagree on method shape.");
        }
    }

    private static void ValidateEmissionDescriptorPolicy(InterceptorEmissionDescriptor descriptor)
    {
        ValidateSingleBodyDescriptor(descriptor);
        ValidateMethodShape(descriptor);

        if (descriptor.DurationPolicy is InterceptorDurationPolicy.RuntimeMetric &&
            descriptor.SignalOwnership is not InterceptorSignalOwnership.TraceAndMetric)
        {
            throw new InvalidOperationException("Runtime metric duration policy requires trace+metric ownership: " + descriptor.Kind);
        }

        if (descriptor.SignalOwnership is InterceptorSignalOwnership.TraceAndMetric &&
            descriptor.DurationPolicy is not InterceptorDurationPolicy.RuntimeMetric)
        {
            throw new InvalidOperationException("Trace+metric ownership requires runtime metric duration policy: " + descriptor.Kind);
        }

        if (descriptor.HttpWebRequestBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.TraceAndMetric,
                InterceptorErrorPolicy.HttpStatusAndException,
                InterceptorDurationPolicy.RuntimeMetric);
            return;
        }

        if (descriptor.DbCommandBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.TraceAndMetric,
                InterceptorErrorPolicy.Exception,
                InterceptorDurationPolicy.RuntimeMetric);
            return;
        }

        if (descriptor.GrpcClientBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.Trace,
                InterceptorErrorPolicy.GrpcStatusAndException,
                InterceptorDurationPolicy.None);
            return;
        }

        if (descriptor.MeterProviderBuilderBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.Metric,
                InterceptorErrorPolicy.None,
                InterceptorDurationPolicy.None);
            return;
        }

        if (descriptor.LoggerBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.Log,
                InterceptorErrorPolicy.RuntimeDelegate,
                InterceptorDurationPolicy.None);
            return;
        }

        if (descriptor.ExternalLoggerBody.IsDefined)
        {
            ValidatePolicy(
                descriptor,
                InterceptorSignalOwnership.Log,
                InterceptorErrorPolicy.Exception,
                InterceptorDurationPolicy.None);
            return;
        }

        if (descriptor.TraceBody.IsDefined)
        {
            if (descriptor.SignalOwnership is not (InterceptorSignalOwnership.Trace or InterceptorSignalOwnership.TraceAndMetric))
                throw new InvalidOperationException("Trace body descriptor must own traces: " + descriptor.Kind);
            if (descriptor.ErrorPolicy is not InterceptorErrorPolicy.Exception)
                throw new InvalidOperationException("Trace body descriptor must use exception error policy: " + descriptor.Kind);
            if (!descriptor.TraceBody.RuntimeHelper.IsDefined)
                throw new InvalidOperationException("Trace body descriptor must provide a runtime helper: " + descriptor.Kind);
            if (descriptor.DurationPolicy is InterceptorDurationPolicy.RuntimeMetric &&
                !descriptor.TraceBody.DurationMetric.IsDefined)
            {
                throw new InvalidOperationException("Trace runtime metric descriptor must provide a duration metric descriptor: " + descriptor.Kind);
            }

            return;
        }

        if (descriptor.ForwardingBody.IsDefined)
        {
            if (descriptor.SignalOwnership is InterceptorSignalOwnership.TraceAndMetric)
            {
                ValidatePolicy(
                    descriptor,
                    InterceptorSignalOwnership.TraceAndMetric,
                    InterceptorErrorPolicy.HttpStatusAndException,
                    InterceptorDurationPolicy.RuntimeMetric);
                return;
            }

            if (descriptor.SignalOwnership is InterceptorSignalOwnership.Trace &&
                descriptor.ErrorPolicy is InterceptorErrorPolicy.Exception or InterceptorErrorPolicy.RuntimeDelegate &&
                descriptor.DurationPolicy is InterceptorDurationPolicy.None)
            {
                return;
            }
        }

        throw new InvalidOperationException("Unsupported interceptor emission policy shape: " + descriptor.Kind);
    }

    private static void ValidateSingleBodyDescriptor(InterceptorEmissionDescriptor descriptor)
    {
        var bodyCount = 0;
        if (descriptor.TraceBody.IsDefined)
            bodyCount++;
        if (descriptor.ForwardingBody.IsDefined)
            bodyCount++;
        if (descriptor.HttpWebRequestBody.IsDefined)
            bodyCount++;
        if (descriptor.DbCommandBody.IsDefined)
            bodyCount++;
        if (descriptor.GrpcClientBody.IsDefined)
            bodyCount++;
        if (descriptor.MeterProviderBuilderBody.IsDefined)
            bodyCount++;
        if (descriptor.LoggerBody.IsDefined)
            bodyCount++;
        if (descriptor.ExternalLoggerBody.IsDefined)
            bodyCount++;

        if (bodyCount != 1)
            throw new InvalidOperationException("Interceptor emission descriptor must define exactly one typed body descriptor: " + descriptor.Kind);
    }

    private static void ValidateMethodShape(InterceptorEmissionDescriptor descriptor)
    {
        if (descriptor.HttpWebRequestBody.IsDefined)
        {
            ValidateMethodShape(descriptor, InterceptorMethodShape.AsyncOrSyncValue);
            return;
        }

        if (descriptor.DbCommandBody.IsDefined)
        {
            ValidateMethodShape(descriptor, InterceptorMethodShape.AsyncOrSyncValue);
            return;
        }

        if (descriptor.GrpcClientBody.IsDefined)
        {
            if (descriptor.GrpcClientBody.Shape is GrpcClientCallShape.Unary)
            {
                ValidateMethodShape(descriptor, InterceptorMethodShape.GrpcUnary);
                return;
            }

            if (descriptor.GrpcClientBody.Shape is GrpcClientCallShape.ServerStreaming or
                GrpcClientCallShape.ClientStreaming or
                GrpcClientCallShape.DuplexStreaming)
            {
                ValidateMethodShape(descriptor, InterceptorMethodShape.GrpcStreaming);
                return;
            }

            throw new InvalidOperationException("gRPC client body descriptor has no call shape: " + descriptor.Kind);
        }

        if (descriptor.MeterProviderBuilderBody.IsDefined)
        {
            ValidateMethodShape(descriptor, InterceptorMethodShape.BuilderRegistration);
            return;
        }

        if (descriptor.LoggerBody.IsDefined || descriptor.ExternalLoggerBody.IsDefined)
        {
            ValidateMethodShape(descriptor, InterceptorMethodShape.Void);
            return;
        }

        if (descriptor.TraceBody.IsDefined)
        {
            if (descriptor.MethodShape is not (
                    InterceptorMethodShape.AsyncOrSyncValue or
                    InterceptorMethodShape.AsyncOrSyncVoid or
                    InterceptorMethodShape.AsyncTask or
                    InterceptorMethodShape.AsyncValue))
            {
                throw new InvalidOperationException("Trace body descriptor has unsupported method shape: " + descriptor.Kind);
            }

            return;
        }

        if (descriptor.ForwardingBody.IsDefined)
        {
            if (descriptor.MethodShape is not (
                    InterceptorMethodShape.AsyncValue or
                    InterceptorMethodShape.AsyncTask or
                    InterceptorMethodShape.BuilderInitialization or
                    InterceptorMethodShape.EndpointRegistration))
            {
                throw new InvalidOperationException("Forwarding body descriptor has unsupported method shape: " + descriptor.Kind);
            }

            return;
        }

        throw new InvalidOperationException("Interceptor emission descriptor has no typed body descriptor: " + descriptor.Kind);
    }

    private static void ValidateMethodShape(
        InterceptorEmissionDescriptor descriptor,
        InterceptorMethodShape methodShape)
    {
        if (descriptor.MethodShape != methodShape)
            throw new InvalidOperationException("Interceptor emission descriptor method shape mismatch: " + descriptor.Kind);
    }

    private static void ValidatePolicy(
        InterceptorEmissionDescriptor descriptor,
        InterceptorSignalOwnership signalOwnership,
        InterceptorErrorPolicy errorPolicy,
        InterceptorDurationPolicy durationPolicy)
    {
        if (descriptor.SignalOwnership != signalOwnership ||
            descriptor.ErrorPolicy != errorPolicy ||
            descriptor.DurationPolicy != durationPolicy)
        {
            throw new InvalidOperationException(
                "Interceptor emission descriptor policy mismatch: " + descriptor.Kind);
        }
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
        InterceptedInvocation invocation,
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
        InterceptedInvocation invocation,
        int index,
        TraceInterceptorBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        var runtimeObservesAsync = ShouldRuntimeObserveAsync(target, descriptor);
        var signatureIsAsync = target.IsAsync && !runtimeObservesAsync;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            GetTraceMethodPrefix(target, descriptor),
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
        descriptor.AppendStartActivity(builder, target);
        builder.AppendLine(";");
        if (descriptor.ActivityEnrichment.IsDefined)
            descriptor.ActivityEnrichment.Append(builder, target);
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        EmitTraceInvocation(builder, target, descriptor);

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.AppendLine(descriptor.GetRecordExceptionStatement());
        if (descriptor.DurationMetric.IsDefined)
            descriptor.DurationMetric.AppendRecordDurationStatement(builder, target);
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
        InterceptorTarget target,
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
        InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
        => descriptor.AsyncObservation.IsDefined &&
           descriptor.AsyncObservation.AppliesTo(target);

    private static void EmitTraceInvocation(
        StringBuilder builder,
        InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
    {
        if (target.IsAsync && ShouldRuntimeObserveAsync(target, descriptor))
        {
            builder.Append("                var resultTask = ");
            AppendInvocationCall(builder, target, descriptor.ReceiverName);
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
                AppendInvocationCall(builder, target, descriptor.ReceiverName);
                builder.AppendLine(".ConfigureAwait(false);");
                EmitTraceSuccessDurationMetric(builder, target, descriptor);
                return;
            }

            builder.Append("                var result = await ");
            AppendInvocationCall(builder, target, descriptor.ReceiverName);
            builder.AppendLine(".ConfigureAwait(false);");
            EmitTraceSuccessDurationMetric(builder, target, descriptor);
            builder.AppendLine("                return result;");
            return;
        }

        if (string.Equals(target.ReturnType, "void", StringComparison.Ordinal))
        {
            builder.Append("                ");
            AppendInvocationCall(builder, target, descriptor.ReceiverName);
            builder.AppendLine(";");
            EmitTraceSuccessDurationMetric(builder, target, descriptor);
            return;
        }

        builder.Append("                var result = ");
        AppendInvocationCall(builder, target, descriptor.ReceiverName);
        builder.AppendLine(";");
        EmitTraceSuccessDurationMetric(builder, target, descriptor);
        builder.AppendLine("                return result;");
    }

    private static void EmitTraceSuccessDurationMetric(
        StringBuilder builder,
        InterceptorTarget target,
        TraceInterceptorBodyDescriptor descriptor)
    {
        if (descriptor.DurationMetric.IsDefined)
            descriptor.DurationMetric.AppendRecordDurationStatement(builder, target);
    }

    private static bool IsTaskLikeReturnWithoutResult(string returnType)
        => returnType is "global::System.Threading.Tasks.Task" or
            "global::System.Threading.Tasks.ValueTask";

    private static void AppendTraceStartActivityArguments(
        StringBuilder builder,
        InterceptorTarget target,
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
                AppendRabbitMqExchangeExpression(builder, target);
                return;
            default:
                throw new InvalidOperationException("Unknown trace start activity argument kind: " + argumentKind);
        }
    }

    private static void EmitHttpWebRequestInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
        int index,
        HttpWebRequestBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName, target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.Append("            var httpWebRequest = (");
        builder.Append(descriptor.RequestType);
        builder.Append(')');
        builder.Append(descriptor.ReceiverName);
        builder.AppendLine(";");
        builder.Append("            var metricStartTimeUtc = ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.GetStartTimeUtcMethod);
        builder.AppendLine("();");
        builder.Append("            var activity = ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.StartActivityMethod);
        builder.Append("(httpWebRequest, ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var result = await ");
            builder.Append(descriptor.ReceiverName);
            builder.Append('.');
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(").ConfigureAwait(false);");
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
        }

        builder.Append("                ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.RecordResultMethod);
        builder.AppendLine("(activity, metricStartTimeUtc, httpWebRequest.Method, result);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.Append("                ");
        builder.Append(descriptor.HelperType);
        builder.Append('.');
        builder.Append(descriptor.RecordExceptionMethod);
        builder.AppendLine("(activity, metricStartTimeUtc, httpWebRequest.Method, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        EmitActivityDisposeFinally(builder);
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitDbCommandInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
        int index,
        DbCommandBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName, target.Parameters, isAsync: false);
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

    private static void EmitMeterProviderBuilderAddMeterInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
        int index,
        MeterProviderBuilderBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName, target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.Append("            var result = ");
        AppendInvocationCall(builder, target, descriptor.ReceiverName);
        builder.AppendLine(";");
        builder.Append("            var qylMeters = ");
        builder.Append(descriptor.EnabledMeterNamesExpression);
        builder.AppendLine(";");
        if (string.IsNullOrEmpty(target.ExtensionContainingType))
        {
            builder.AppendLine("            return qylMeters.Length is 0 ? result : result.AddMeter(qylMeters);");
        }
        else
        {
            builder.Append("            return qylMeters.Length is 0 ? result : ");
            builder.Append(target.ExtensionContainingType);
            builder.Append('.');
            builder.Append(target.MethodName);
            builder.AppendLine("(result, qylMeters);");
        }

        builder.AppendLine();
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
        int index,
        GrpcClientBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, descriptor.ReceiverName, target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        EmitGrpcCallPreamble(builder, target, descriptor);
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
                builder.AppendLine(".ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
                break;

            case GrpcClientCallShape.ServerStreaming:
                builder.AppendLine("                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
                break;

            case GrpcClientCallShape.ClientStreaming:
                builder.AppendLine("                    call.RequestStream,");
                builder.Append("                    ");
                builder.Append(descriptor.HelperType);
                builder.AppendLine(".ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
                break;

            case GrpcClientCallShape.DuplexStreaming:
                builder.AppendLine("                    call.RequestStream,");
                builder.AppendLine("                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
                break;

            default:
                throw new InvalidOperationException("Unknown gRPC client call shape: " + descriptor.Shape);
        }

        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
    }

    private static void EmitGrpcCallPreamble(StringBuilder builder, InterceptorTarget target, GrpcClientBodyDescriptor descriptor)
    {
        builder.Append("            var activity = ");
        builder.Append(descriptor.HelperType);
        builder.Append(".StartActivity(");
        AppendStringLiteral(builder, target.ReceiverType);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.Append(", ");
        AppendGrpcMetadataExpression(builder, target);
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

    private static void AppendGrpcMetadataExpression(StringBuilder builder, InterceptorTarget target)
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
        builder.AppendLine("        public static global::Grpc.Core.IAsyncStreamReader<T> Create<T>(global::Grpc.Core.IAsyncStreamReader<T> inner, global::System.Diagnostics.Activity? activity, global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? responseHeadersTask)");
        builder.AppendLine("            => new QylObservedAsyncStreamReader<T>(inner, activity, responseHeadersTask);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    internal sealed class QylObservedAsyncStreamReader<T> : global::Grpc.Core.IAsyncStreamReader<T>");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::Grpc.Core.IAsyncStreamReader<T> _inner;");
        builder.AppendLine("        private readonly global::System.Diagnostics.Activity? _activity;");
        builder.AppendLine("        private readonly global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? _responseHeadersTask;");
        builder.AppendLine("        private bool _completed;");
        builder.AppendLine();
        builder.AppendLine("        public QylObservedAsyncStreamReader(global::Grpc.Core.IAsyncStreamReader<T> inner, global::System.Diagnostics.Activity? activity, global::System.Threading.Tasks.Task<global::Grpc.Core.Metadata>? responseHeadersTask)");
        builder.AppendLine("        {");
        builder.AppendLine("            _inner = inner;");
        builder.AppendLine("            _activity = activity;");
        builder.AppendLine("            _responseHeadersTask = responseHeadersTask;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public T Current => _inner.Current;");
        builder.AppendLine();
        builder.AppendLine("        public async global::System.Threading.Tasks.Task<bool> MoveNext(global::System.Threading.CancellationToken cancellationToken)");
        builder.AppendLine("        {");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                var hasNext = await _inner.MoveNext(cancellationToken).ConfigureAwait(false);");
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

    private static void AppendKafkaTopicExpression(StringBuilder builder, InterceptorTarget target)
    {
        if (target.Parameters.Length is 0)
        {
            builder.Append("null");
            return;
        }

        if (string.Equals(target.Parameters[0].TypeName, "string", StringComparison.Ordinal) ||
            string.Equals(target.Parameters[0].TypeName, "global::System.String", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            return;
        }

        if (string.Equals(target.Parameters[0].TypeName, "global::Confluent.Kafka.TopicPartition", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            builder.Append(".Topic");
            return;
        }

        builder.Append("null");
    }

    private static void AppendGraphQlDocumentCaptureExpression(StringBuilder builder, InterceptorTarget target)
    {
        if (target.Parameters.Length > 0 && string.Equals(target.Parameters[0].TypeName, "global::GraphQL.ExecutionOptions", StringComparison.Ordinal))
        {
            builder.Append("global::Qyl.AutoInstrumentation.QylAutoInstrumentationOptions.Current.GraphQlSetDocument && ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(" is not null ? ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(".Query");
            builder.Append(" : null");
            return;
        }

        builder.Append("null");
    }

    private static void AppendGraphQlOperationNameExpression(StringBuilder builder, InterceptorTarget target)
    {
        if (target.Parameters.Length > 0 && string.Equals(target.Parameters[0].TypeName, "global::GraphQL.ExecutionOptions", StringComparison.Ordinal))
        {
            builder.Append(target.Parameters[0].Name);
            builder.Append(" is not null ? ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(".OperationName : null");
            return;
        }

        builder.Append("null");
    }

    private static void AppendRabbitMqExchangeExpression(StringBuilder builder, InterceptorTarget target)
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
        InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        if (descriptor.Kind is LoggerInterceptorBodyKind.ILoggerLog)
        {
            EmitDirectLoggerInterceptor(builder, invocation, index, descriptor);
            return;
        }

        EmitLoggerExtensionInterceptor(builder, invocation, index, descriptor);
    }

    private static void EmitDirectLoggerInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        var attribute = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptsLocationAttributeSyntax(invocation.Location);
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
        InterceptedInvocation invocation,
        int index,
        LoggerBodyDescriptor descriptor)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, "void", descriptor.MethodPrefix + "_" + target.MethodName, index, target.ReceiverType, "logger", target.Parameters, isAsync: false);
        builder.Append("            => ");
        builder.Append(descriptor.HelperType);
        builder.Append(".LogExtension(logger, ");
        AppendLoggerLevelExpression(builder, target);
        builder.Append(", ");
        AppendFirstParameterExpression(builder, target, "global::Microsoft.Extensions.Logging.EventId", "default");
        builder.Append(", ");
        AppendFirstParameterExpression(builder, target, "global::System.Exception", "null");
        builder.Append(", ");
        AppendFirstParameterExpression(builder, target, "global::System.String", "null");
        builder.Append(", ");
        AppendFirstArrayParameterExpression(builder, target, "global::System.Object", "global::System.Array.Empty<object>()");
        builder.AppendLine(");");
        builder.AppendLine();
    }

    private static void EmitExternalLoggerInterceptor(
        StringBuilder builder,
        InterceptedInvocation invocation,
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
        AppendExternalLoggerSeverityExpression(builder, target);
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

    private static void AppendExternalLoggerSeverityExpression(StringBuilder builder, InterceptorTarget target)
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

    private static void AppendLoggerLevelExpression(StringBuilder builder, InterceptorTarget target)
    {
        if (string.Equals(target.MethodName, "Log", StringComparison.Ordinal))
        {
            AppendFirstParameterExpression(builder, target, "global::Microsoft.Extensions.Logging.LogLevel", "global::Microsoft.Extensions.Logging.LogLevel.None");
            return;
        }

        var levelName = target.MethodName switch
        {
            "LogTrace" => "Trace",
            "LogDebug" => "Debug",
            "LogInformation" => "Information",
            "LogWarning" => "Warning",
            "LogError" => "Error",
            "LogCritical" => "Critical",
            _ => "None",
        };
        builder.Append("global::Microsoft.Extensions.Logging.LogLevel.");
        builder.Append(levelName);
    }

    private static void AppendFirstParameterExpression(StringBuilder builder, InterceptorTarget target, string typeName, string fallbackExpression)
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

    private static void AppendFirstArrayParameterExpression(StringBuilder builder, InterceptorTarget target, string elementTypeName, string fallbackExpression)
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
        => string.Equals(NormalizeSpecialTypeName(parameter.TypeName), NormalizeSpecialTypeName(typeName), StringComparison.Ordinal);

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
        => EmitAttributeAndSignature(builder, location, returnType, methodPrefix, index, receiverType, receiverName, parameters, isAsync, string.Empty, string.Empty);

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
        var attribute = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptsLocationAttributeSyntax(location);
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

    private static void AppendArgumentList(StringBuilder builder, EquatableArray<ParameterSpec> parameters, bool includeLeadingComma)
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

    private static void AppendInvocationCall(StringBuilder builder, InterceptorTarget target, string receiverName)
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

    private static bool TryGetHttpWebRequestInvocation(IMethodSymbol symbol, ITypeSymbol? receiverType, out InterceptorTarget target)
    {
        target = default;
        var effectiveReceiverType = IsType(symbol.ContainingType, "global::System.Net.HttpWebRequest")
            ? symbol.ContainingType
            : receiverType;

        if (effectiveReceiverType is null ||
            !IsType(effectiveReceiverType, "global::System.Net.HttpWebRequest") ||
            !TryGetNoParameters(symbol, out var parameters))
        {
            return false;
        }

        var methodName = symbol.Name;
        var isAsync = methodName.EndsWithOrdinal("Async");
        if (!TryGetHttpWebRequestReturn(symbol, methodName, isAsync, out var returnType))
            return false;

        target = new InterceptorTarget(
            InterceptorKind.HttpWebRequest,
            "signals.traces.HTTPCLIENT",
            "HTTPCLIENT",
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            isAsync,
            AdditionalContractKeys: ContractKeys("signals.metrics.HTTPCLIENT"));
        return true;
    }

    private static bool TryGetDbCommandInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!InheritsFromOrIs(symbol.ContainingType, "global::System.Data.Common.DbCommand"))
            return false;

        var methodName = symbol.Name;
        var isAsync = methodName.EndsWithOrdinal("Async");
        if (!TryGetDbCommandParameters(symbol, methodName, out var parameters))
            return false;

        if (!TryGetDbCommandReturn(symbol, methodName, isAsync, out var returnType))
            return false;

        var instrumentationId = GetDbInstrumentationId(symbol.ContainingType);
        target = new InterceptorTarget(
            InterceptorKind.DbCommand,
            GetDbTraceContractKey(instrumentationId),
            instrumentationId,
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            isAsync,
            AdditionalContractKeys: GetDbMetricContractKeys(instrumentationId));
        return true;
    }

    private static bool TryGetAspNetCoreRequestDelegateInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!string.Equals(symbol.Name, "Invoke", StringComparison.Ordinal) ||
            !IsType(symbol.ContainingType, "global::Microsoft.AspNetCore.Http.RequestDelegate") ||
            !IsTask(symbol.ReturnType) ||
            symbol.Parameters.Length is not 1 ||
            !IsType(symbol.Parameters[0].Type, "global::Microsoft.AspNetCore.Http.HttpContext"))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.AspNetCoreRequestDelegate,
            "signals.traces.ASPNETCORE",
            "ASPNETCORE",
            CleanTypeName(symbol.ContainingType),
            "Invoke",
            "global::System.Threading.Tasks.Task",
            Parameters(symbol),
            false);
        return true;
    }

    private static bool TryGetAspNetCoreWebApplicationBuilderBuildInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (symbol.IsStatic ||
            !string.Equals(symbol.Name, "Build", StringComparison.Ordinal) ||
            !IsType(symbol.ContainingType, "global::Microsoft.AspNetCore.Builder.WebApplicationBuilder") ||
            !IsType(symbol.ReturnType, "global::Microsoft.AspNetCore.Builder.WebApplication") ||
            symbol.Parameters.Length is not 0)
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.AspNetCoreWebApplicationBuilderBuild,
            "signals.traces.ASPNETCORE",
            "ASPNETCORE",
            CleanTypeName(symbol.ContainingType),
            "Build",
            "global::Microsoft.AspNetCore.Builder.WebApplication",
            Parameters(symbol),
            false);
        return true;
    }

    private static bool TryGetAspNetCoreEndpointMapInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        var original = symbol.ReducedFrom;

        if (original is null ||
            !IsSupportedAspNetCoreMapMethod(symbol.Name) ||
            !IsType(original.ContainingType, "global::Microsoft.AspNetCore.Builder.EndpointRouteBuilderExtensions") ||
            !TryGetAspNetCoreEndpointMapReturnType(symbol, out var returnType))
            return false;

        target = new InterceptorTarget(
            InterceptorKind.AspNetCoreEndpointMap,
            "signals.traces.ASPNETCORE",
            "ASPNETCORE",
            "global::Microsoft.AspNetCore.Routing.IEndpointRouteBuilder",
            symbol.Name,
            returnType,
            Parameters(symbol),
            false);
        return true;
    }

    private static bool TryGetAspNetCoreEndpointMapReturnType(IMethodSymbol symbol, out string returnType)
    {
        returnType = string.Empty;

        var handlerIndex = 1;
        if (string.Equals(symbol.Name, "MapMethods", StringComparison.Ordinal))
        {
            if (symbol.Parameters.Length is not 3 ||
                !IsType(symbol.Parameters[0].Type, "global::System.String") ||
                !IsConstructedFrom(symbol.Parameters[1].Type, "global::System.Collections.Generic.IEnumerable<T>"))
            {
                return false;
            }

            handlerIndex = 2;
        }
        else if (symbol.Parameters.Length is not 2 ||
                 !IsType(symbol.Parameters[0].Type, "global::System.String"))
        {
            return false;
        }

        if (IsType(symbol.Parameters[handlerIndex].Type, "global::Microsoft.AspNetCore.Http.RequestDelegate") &&
            IsType(symbol.ReturnType, "global::Microsoft.AspNetCore.Builder.IEndpointConventionBuilder"))
        {
            returnType = "global::Microsoft.AspNetCore.Builder.IEndpointConventionBuilder";
            return true;
        }

        return false;
    }

    private static bool IsSupportedAspNetCoreMapMethod(string name)
        => name is "MapGet" or "MapPost" or "MapPut" or "MapDelete" or "MapPatch" or "MapMethods";

    private static bool TryGetMeterProviderBuilderAddMeterInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        ITypeSymbol receiverType = symbol.ContainingType;
        var extensionContainingType = string.Empty;
        if (symbol.ReducedFrom is { Parameters.Length: > 0 } original)
        {
            receiverType = original.Parameters[0].Type;
            extensionContainingType = CleanTypeName(original.ContainingType);
        }
        else if (symbol.IsStatic)
        {
            return false;
        }

        if (!string.Equals(symbol.Name, "AddMeter", StringComparison.Ordinal) ||
            !IsType(receiverType, "global::OpenTelemetry.Metrics.MeterProviderBuilder") ||
            !IsType(symbol.ReturnType, "global::OpenTelemetry.Metrics.MeterProviderBuilder") ||
            symbol.Parameters.Length is not 1 ||
            !IsArrayOf(symbol.Parameters[0].Type, "global::System.String"))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.MeterProviderBuilderAddMeter,
            "signals.metrics.ASPNETCORE",
            "ASPNETCORE",
            CleanTypeName(receiverType),
            "AddMeter",
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
            false,
            ExtensionContainingType: extensionContainingType,
            AdditionalContractKeys: ContractKeys(
                "signals.metrics.HTTPCLIENT",
                "signals.metrics.NETRUNTIME",
                "signals.metrics.NPGSQL",
                "signals.metrics.NSERVICEBUS",
                "signals.metrics.PROCESS",
                "signals.metrics.SQLCLIENT"));
        return true;
    }

    private static bool TryGetAzureClientInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
            !CanEmitByValueOrInParameters(symbol) ||
            !IsAzureClientType(symbol.ContainingType) ||
            !TryGetAzureClientOperationReturn(symbol.ReturnType, out var isAsync))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.AzureClient,
            "signals.traces.AZURE",
            "AZURE",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
            isAsync);
        return true;
    }

    private static bool IsAzureClientType(ITypeSymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol named ||
            !named.Name.EndsWithOrdinal("Client"))
        {
            return false;
        }

        // Require the client to come from a real Azure SDK assembly (Azure.Storage.Blobs,
        // Azure.Messaging.*, …) — not merely any user type parked in an `Azure.*` namespace.
        // Together with the Azure.Response return-type gate this stops a false-positive Azure span
        // on a user-authored *Client placed in the reserved Azure root namespace.
        var namespaceName = named.ContainingNamespace.ToDisplayString();
        return namespaceName.StartsWithOrdinal("Azure.") &&
               !namespaceName.StartsWithOrdinal("Azure.Core") &&
               named.ContainingAssembly?.Name is { } assemblyName &&
               assemblyName.StartsWithOrdinal("Azure.");
    }

    private static bool TryGetAzureClientOperationReturn(ITypeSymbol returnType, out bool isAsync)
    {
        isAsync = false;
        if (IsAzureResponseType(returnType))
            return true;

        if (TryGetTaskResult(returnType, out var resultType) && IsAzureResponseType(resultType))
        {
            isAsync = true;
            return true;
        }

        return false;
    }

    private static bool IsAzureResponseType(ITypeSymbol? symbol)
        => symbol is INamedTypeSymbol named &&
           (IsTypeByMetadata(named, "Azure", "Response") ||
            IsConstructedGeneric(named, "Azure", "Response`1"));

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
            "signals.traces.ELASTICSEARCH",
            "ELASTICSEARCH",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
            "signals.traces.ELASTICTRANSPORT",
            "ELASTICTRANSPORT",
            CleanTypeName(receiverType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
            "signals.traces.WCFCLIENT",
            "WCFCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
            "signals.traces.GRPCNETCLIENT",
            "GRPCNETCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
            "signals.traces.GRPCNETCLIENT",
            "GRPCNETCLIENT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
            "signals.traces.MASSTRANSIT",
            "MASSTRANSIT",
            CleanTypeName(receiverType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
        var parameters = Parameters(symbol);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromVisibleTypes(symbol, receiverType);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromFormattedTypes(receiverTypeName, returnTypeName, parameters);

        target = new InterceptorTarget(
            InterceptorKind.NServiceBusMessageOperation,
            "signals.traces.NSERVICEBUS",
            "NSERVICEBUS",
            receiverTypeName,
            symbol.Name,
            returnTypeName,
            parameters,
            true,
            typeParameterList,
            GetConstraintClauses(symbol),
            GetReducedExtensionContainingType(symbol),
            AdditionalContractKeys: ContractKeys("signals.metrics.NSERVICEBUS"));
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
            "signals.traces.QUARTZ",
            "QUARTZ",
            CleanTypeName(symbol.ContainingType),
            "Execute",
            "global::System.Threading.Tasks.Task",
            Parameters(symbol),
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
            "signals.traces.STACKEXCHANGEREDIS",
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
            "signals.traces.GRAPHQL",
            "GRAPHQL",
            CleanTypeName(symbol.ContainingType),
            "ExecuteAsync",
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
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
        var parameters = Parameters(symbol);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromVisibleTypes(symbol, receiverType);
        if (string.IsNullOrEmpty(typeParameterList))
            typeParameterList = GetTypeParameterListFromFormattedTypes(receiverTypeName, returnTypeName, parameters);

        target = new InterceptorTarget(
            InterceptorKind.MongoDbCollection,
            "signals.traces.MONGODB",
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
                "signals.traces.RABBITMQ",
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
                "signals.traces.RABBITMQ",
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
            "signals.logs.ILOGGER",
            "ILOGGER",
            CleanTypeName(symbol.ContainingType),
            "Log",
            "void",
            Parameters(symbol),
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
            "signals.logs.ILOGGER",
            "ILOGGER",
            "global::Microsoft.Extensions.Logging.ILogger",
            symbol.Name,
            "void",
            Parameters(symbol),
            false);
        return true;
    }

    private static bool IsSupportedLoggerExtensionName(string name)
        => name is "Log" or
            "LogTrace" or
            "LogDebug" or
            "LogInformation" or
            "LogWarning" or
            "LogError" or
            "LogCritical";

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
            "signals.logs.NLOG",
            "NLOG",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            "void",
            Parameters(symbol),
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
            "signals.logs.LOG4NET",
            "LOG4NET",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            "void",
            Parameters(symbol),
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
                "signals.traces.KAFKA",
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
                "signals.traces.KAFKA",
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
            "signals.traces.KAFKA",
            "KAFKA",
            CleanTypeName(symbol.ContainingType),
            "Consume",
            CleanTypeName(symbol.ReturnType, symbol),
            parameters,
            false);
        return true;
    }

    private static bool TryGetEntityFrameworkCoreDbContextInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!InheritsFromOrIs(symbol.ContainingType, "global::Microsoft.EntityFrameworkCore.DbContext"))
            return false;

        if (string.Equals(symbol.Name, "SaveChanges", StringComparison.Ordinal) &&
            symbol.ReturnType.SpecialType is SpecialType.System_Int32 &&
            TryGetEfCoreSaveChangesParameters(symbol, allowCancellationToken: false, out var parameters))
        {
            target = new InterceptorTarget(
                InterceptorKind.EntityFrameworkCoreDbContext,
                "signals.traces.ENTITYFRAMEWORKCORE",
                "ENTITYFRAMEWORKCORE",
                CleanTypeName(symbol.ContainingType),
                "SaveChanges",
                "int",
                parameters,
                false);
            return true;
        }

        if (string.Equals(symbol.Name, "SaveChangesAsync", StringComparison.Ordinal) &&
            IsTaskOf(symbol.ReturnType, "global::System.Int32") &&
            TryGetEfCoreSaveChangesParameters(symbol, allowCancellationToken: true, out parameters))
        {
            target = new InterceptorTarget(
                InterceptorKind.EntityFrameworkCoreDbContext,
                "signals.traces.ENTITYFRAMEWORKCORE",
                "ENTITYFRAMEWORKCORE",
                CleanTypeName(symbol.ContainingType),
                "SaveChangesAsync",
                "global::System.Threading.Tasks.Task<int>",
                parameters,
                true);
            return true;
        }

        return false;
    }

    private static bool TryGetEntityFrameworkCoreQueryableInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        var original = symbol.ReducedFrom;
        if (original is null ||
            !IsType(original.ContainingType, "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions") ||
            !IsSupportedEntityFrameworkCoreQueryableMethod(symbol.Name) ||
            !CanEmitByValueOrInParameters(symbol) ||
            original.Parameters.Length is 0 ||
            !TryGetEntityFrameworkCoreQueryableReturn(symbol.ReturnType))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.EntityFrameworkCoreQueryable,
            "signals.traces.ENTITYFRAMEWORKCORE",
            "ENTITYFRAMEWORKCORE",
            CleanTypeName(original.Parameters[0].Type),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
            true,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol),
            ExtensionContainingType: "global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions");
        return true;
    }

    private static bool IsSupportedEntityFrameworkCoreQueryableMethod(string methodName)
        => methodName is "ToListAsync" or
            "ToArrayAsync" or
            "FirstAsync" or
            "FirstOrDefaultAsync" or
            "SingleAsync" or
            "SingleOrDefaultAsync" or
            "LastAsync" or
            "LastOrDefaultAsync" or
            "AnyAsync" or
            "AllAsync" or
            "CountAsync" or
            "LongCountAsync" or
            "MinAsync" or
            "MaxAsync" or
            "SumAsync" or
            "AverageAsync" or
            "ContainsAsync" or
            "LoadAsync" or
            "ForEachAsync" or
            "ExecuteDeleteAsync" or
            "ExecuteUpdateAsync";

    private static bool TryGetEntityFrameworkCoreQueryableReturn(ITypeSymbol returnType)
        => IsTask(returnType) || TryGetTaskResult(returnType, out _);

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
            AdditionalContractKeys: ContractKeys("signals.metrics.HTTPCLIENT"));

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
            "NPGSQL" => ContractKeys("signals.metrics.NPGSQL"),
            "SQLCLIENT" => ContractKeys("signals.metrics.SQLCLIENT"),
            _ => default,
        };

    private static EquatableArray<string> ContractKeys(params string[] contractKeys)
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
            parameters = Parameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 2)
        {
            if (IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken") ||
                IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption"))
            {
                parameters = Parameters(symbol);
                return true;
            }
        }

        if (symbol.Parameters.Length is 3 &&
            IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption") &&
            IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = Parameters(symbol);
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
            parameters = Parameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 2)
        {
            if (IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken") ||
                (allowCompletionOption && IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption")))
            {
                parameters = Parameters(symbol);
                return true;
            }
        }

        if (allowCompletionOption &&
            symbol.Parameters.Length is 3 &&
            IsType(symbol.Parameters[1].Type, "global::System.Net.Http.HttpCompletionOption") &&
            IsType(symbol.Parameters[2].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = Parameters(symbol);
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
            parameters = Parameters(symbol);
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
            parameters = Parameters(symbol);
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
                parameters = Parameters(symbol);
                return true;
            }
        }

        if (allowCancellationToken &&
            symbol.Parameters.Length is 2 &&
            IsType(symbol.Parameters[0].Type, "global::System.Data.CommandBehavior") &&
            IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = Parameters(symbol);
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

        parameters = Parameters(symbol);
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
            parameters = Parameters(symbol);
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

            parameters = Parameters(symbol);
            return true;
        }

        if (symbol.Parameters.Length is 0)
            return false;

        if (!IsType(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey") &&
            !IsArrayOf(symbol.Parameters[0].Type, "global::StackExchange.Redis.RedisKey"))
        {
            return false;
        }

        parameters = Parameters(symbol);
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

        parameters = Parameters(symbol);
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
                parameters = Parameters(symbol);
                return true;
            }
        }

        if (allowCancellationToken &&
            symbol.Parameters.Length is 2 &&
            symbol.Parameters[0].Type.SpecialType is SpecialType.System_Boolean &&
            IsType(symbol.Parameters[1].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = Parameters(symbol);
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
            parameters = Parameters(symbol);
            return true;
        }

        if (symbol.Parameters[0].Type is INamedTypeSymbol publicationAddress &&
            IsTypeByMetadata(publicationAddress, "RabbitMQ.Client", "PublicationAddress"))
        {
            parameters = Parameters(symbol);
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

    private static EquatableArray<ParameterSpec> Parameters(IMethodSymbol symbol)
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

        if (symbol is ITypeParameterSymbol typeParameter)
            return typeParameter.Name;

        if (symbol is IArrayTypeSymbol array)
            return CleanTypeName(array.ElementType, typeParameters, typeArguments) + "[]";

        if (symbol is INamedTypeSymbol { IsGenericType: true } named && named.TypeArguments.Length > 0)
        {
            var constructedName = named.ConstructedFrom.ToDisplayString(s_fullyQualifiedFormat);
            var genericStart = constructedName.IndexOf('<');
            var typeName = genericStart < 0 ? constructedName : constructedName.Substring(0, genericStart);
            var arguments = named.TypeArguments
                .Select(typeArgument => CleanTypeName(typeArgument, typeParameters, typeArguments));
            return typeName + "<" + string.Join(", ", arguments) + ">";
        }

        return CleanTypeName(symbol);
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
        builder.Append(value.Replace("\\", "\\\\").Replace("\"", "\\\""));
        builder.Append('"');
    }

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
