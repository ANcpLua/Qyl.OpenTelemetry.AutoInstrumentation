using System.Collections.Immutable;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Text;

namespace Qyl.AutoInstrumentation.SourceGenerators;

[Generator(LanguageNames.CSharp)]
public sealed class QylAutoInstrumentationGenerator : IIncrementalGenerator
{
    private static readonly SymbolDisplayFormat FullyQualifiedFormat =
        SymbolDisplayFormat.FullyQualifiedFormat.WithMiscellaneousOptions(
            SymbolDisplayFormat.FullyQualifiedFormat.MiscellaneousOptions &
            ~SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

    public void Initialize(IncrementalGeneratorInitializationContext context)
    {
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

        context.RegisterSourceOutput(interceptedInvocations, static (sourceContext, invocations) =>
        {
            EmitInterceptors(sourceContext, invocations);
        });
    }

    private static InterceptedInvocation? TryCreateInterceptedInvocation(
        GeneratorSyntaxContext context,
        CancellationToken cancellationToken)
    {
        var invocation = (InvocationExpressionSyntax)context.Node;
        if (string.Equals(context.SemanticModel.Compilation.AssemblyName, "Qyl.AutoInstrumentation", StringComparison.Ordinal))
            return null;

        if (context.SemanticModel.GetInterceptorMethod(invocation, cancellationToken) is not null)
            return null;

        if (context.SemanticModel.GetSymbolInfo(invocation, cancellationToken).Symbol is not IMethodSymbol symbol)
            return null;

        var receiverType = GetInvocationReceiverType(invocation, context.SemanticModel, cancellationToken);
        if (!TryGetInvocation(symbol, receiverType, out var target))
            return null;

        if (InstrumentationContract.TryGetSupportedSignal(target.ContractKey) is null)
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
        if (TryGetHttpClientInvocation(symbol, out target))
            return true;

        if (TryGetHttpWebRequestInvocation(symbol, receiverType, out target))
            return true;

        if (TryGetAspNetCoreWebApplicationBuilderBuildInvocation(symbol, out target))
            return true;

        if (TryGetAspNetCoreRequestDelegateInvocation(symbol, out target))
            return true;

        if (TryGetAspNetCoreEndpointMapInvocation(symbol, out target))
            return true;

        if (TryGetMeterProviderBuilderAddMeterInvocation(symbol, out target))
            return true;

        if (TryGetAzureClientInvocation(symbol, out target))
            return true;

        if (TryGetElasticInvocation(symbol, out target))
            return true;

        if (TryGetWcfClientInvocation(symbol, out target))
            return true;

        if (TryGetWcfCoreServiceModelServicesInvocation(symbol, out target))
            return true;

        if (TryGetGrpcNetClientAsyncUnaryInvocation(symbol, out target))
            return true;

        if (TryGetGrpcNetClientStreamingInvocation(symbol, out target))
            return true;

        if (TryGetKafkaInvocation(symbol, out target))
            return true;

        if (TryGetMassTransitInvocation(symbol, out target))
            return true;

        if (TryGetNServiceBusInvocation(symbol, out target))
            return true;

        if (TryGetQuartzInvocation(symbol, out target))
            return true;

        if (TryGetStackExchangeRedisInvocation(symbol, out target))
            return true;

        if (TryGetGraphQlInvocation(symbol, out target))
            return true;

        if (TryGetMongoDbInvocation(symbol, out target))
            return true;

        if (TryGetRabbitMqInvocation(symbol, out target))
            return true;

        if (TryGetLoggerExtensionInvocation(symbol, out target))
            return true;

        if (TryGetLoggerInvocation(symbol, out target))
            return true;

        if (TryGetNLogInvocation(symbol, out target))
            return true;

        if (TryGetLog4NetInvocation(symbol, out target))
            return true;

        if (TryGetEntityFrameworkCoreDbContextInvocation(symbol, out target))
            return true;

        if (TryGetEntityFrameworkCoreQueryableInvocation(symbol, out target))
            return true;

        if (TryGetDbCommandInvocation(symbol, out target))
            return true;

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
            .ToArray();

        if (invocations.Length is 0)
            return;

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated/>");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("#pragma warning disable");
        builder.AppendLine("namespace Qyl.AutoInstrumentation.Generated");
        builder.AppendLine("{");
        builder.AppendLine("    internal static class QylGeneratedInterceptors");
        builder.AppendLine("    {");

        for (var index = 0; index < invocations.Length; index++)
        {
            var invocation = invocations[index];
            if (invocation.Target.Kind is InterceptorKind.HttpClient)
                EmitHttpClientInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.HttpWebRequest)
                EmitHttpWebRequestInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.AspNetCoreWebApplicationBuilderBuild)
                EmitAspNetCoreWebApplicationBuilderBuildInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.AspNetCoreRequestDelegate)
                EmitAspNetCoreRequestDelegateInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.AspNetCoreEndpointMap)
                EmitAspNetCoreEndpointMapInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.MeterProviderBuilderAddMeter)
                EmitMeterProviderBuilderAddMeterInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.AzureClient)
                EmitAzureClientInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.ElasticsearchClient or InterceptorKind.ElasticTransport)
                EmitElasticInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.WcfClient)
                EmitWcfClientInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.WcfCoreServiceModelServices)
                EmitWcfCoreServiceModelServicesInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.GrpcNetClientAsyncUnaryCall)
                EmitGrpcNetClientAsyncUnaryInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.GrpcNetClientAsyncServerStreamingCall)
                EmitGrpcNetClientAsyncServerStreamingInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.GrpcNetClientAsyncClientStreamingCall)
                EmitGrpcNetClientAsyncClientStreamingInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall)
                EmitGrpcNetClientAsyncDuplexStreamingInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.KafkaProducer)
                EmitKafkaProducerInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.KafkaConsumer)
                EmitKafkaConsumerInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.MassTransitMessageOperation)
                EmitMassTransitInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.NServiceBusMessageOperation)
                EmitNServiceBusInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.QuartzJobExecute)
                EmitQuartzInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.StackExchangeRedisCommandAsync)
                EmitStackExchangeRedisInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.GraphQlDocumentExecuter)
                EmitGraphQlInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.MongoDbCollection)
                EmitMongoDbInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.RabbitMqBasicPublish)
                EmitRabbitMqInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.ILoggerExtensionLog)
                EmitLoggerExtensionInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.ILoggerLog)
                EmitLoggerInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.NLogLogger)
                EmitExternalLoggerInterceptor(builder, invocation, index, "log.nlog");
            else if (invocation.Target.Kind is InterceptorKind.Log4NetLogger)
                EmitExternalLoggerInterceptor(builder, invocation, index, "log.log4net");
            else if (invocation.Target.Kind is InterceptorKind.EntityFrameworkCoreDbContext)
                EmitEntityFrameworkCoreDbContextInterceptor(builder, invocation, index);
            else if (invocation.Target.Kind is InterceptorKind.EntityFrameworkCoreQueryable)
                EmitEntityFrameworkCoreQueryableInterceptor(builder, invocation, index);
            else
                EmitDbCommandInterceptor(builder, invocation, index);
        }

        builder.AppendLine("    }");
        if (RequiresCoreWcfServiceBehavior(invocations))
            EmitCoreWcfServiceBehavior(builder);

        if (RequiresGrpcStreamReader(invocations))
            EmitGrpcStreamReaderWrapper(builder);

        builder.AppendLine("}");

        context.AddSource("QylAutoInstrumentation.Interceptors.g.cs", SourceText.From(builder.ToString(), Encoding.UTF8));
    }

    private static void EmitHttpClientInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "HttpClient_" + target.MethodName, index, "global::System.Net.Http.HttpClient", "client", target.Parameters, isAsync: false);
        builder.Append("            => global::Qyl.AutoInstrumentation.QylInterceptedHttpClient.");
        builder.Append(target.MethodName);
        builder.Append("(client");
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
        builder.AppendLine(");");
        builder.AppendLine();
    }

    private static void EmitHttpWebRequestInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "HttpWebRequest_" + target.MethodName, index, target.ReceiverType, "request", target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.AppendLine("            var httpWebRequest = (global::System.Net.HttpWebRequest)request;");
        builder.AppendLine("            var metricStartTimeUtc = global::Qyl.AutoInstrumentation.QylInterceptedHttpWebRequest.GetStartTimeUtc();");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedHttpWebRequest.StartActivity(httpWebRequest, ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var result = await request.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(").ConfigureAwait(false);");
        }
        else
        {
            builder.Append("                var result = request.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
        }

        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedHttpWebRequest.RecordResult(activity, metricStartTimeUtc, httpWebRequest.Method, result);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedHttpWebRequest.RecordException(activity, metricStartTimeUtc, httpWebRequest.Method, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitDbCommandInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "DbCommand_" + target.MethodName, index, target.ReceiverType, "command", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.AppendLine("            var metricStart = global::Qyl.AutoInstrumentation.QylDbClientMetrics.GetTimestamp();");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.StartActivity(command, ");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var resultTask = command.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.Append("                return global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.ObserveAsync(resultTask, activity, metricStart, ");
            AppendStringLiteral(builder, target.InstrumentationId);
            builder.AppendLine(");");
        }
        else
        {
            builder.Append("                var result = command.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");

            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.RecordSuccess(activity);");
            builder.Append("                global::Qyl.AutoInstrumentation.QylDbClientMetrics.RecordDuration(metricStart, ");
            AppendStringLiteral(builder, target.InstrumentationId);
            builder.AppendLine(");");
            builder.AppendLine("                return result;");
        }
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedDbCommand.RecordException(activity, exception);");
        builder.Append("                global::Qyl.AutoInstrumentation.QylDbClientMetrics.RecordDuration(metricStart, ");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.AppendLine(");");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        if (!target.IsAsync)
        {
            builder.AppendLine("            finally");
            builder.AppendLine("            {");
            builder.AppendLine("                activity?.Dispose();");
            builder.AppendLine("            }");
        }
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitAspNetCoreRequestDelegateInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "AspNetCoreRequestDelegate_" + target.MethodName, index, target.ReceiverType, "requestDelegate", target.Parameters, isAsync: false);
        builder.AppendLine("            => global::Qyl.AutoInstrumentation.QylInterceptedAspNetCore.InvokeAsync(requestDelegate, p0);");
        builder.AppendLine();
    }

    private static void EmitAspNetCoreWebApplicationBuilderBuildInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "AspNetCoreWebApplicationBuilder_" + target.MethodName, index, target.ReceiverType, "builder", target.Parameters, isAsync: false);
        builder.AppendLine("            => global::Qyl.AutoInstrumentation.QylInterceptedAspNetCore.Build(builder);");
        builder.AppendLine();
    }

    private static void EmitAspNetCoreEndpointMapInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "AspNetCoreEndpointMap_" + target.MethodName, index, target.ReceiverType, "endpoints", target.Parameters, isAsync: false);
        builder.Append("            => global::Qyl.AutoInstrumentation.QylInterceptedAspNetCore.");
        builder.Append(target.MethodName);
        builder.Append("(endpoints");
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
        builder.AppendLine(");");
        builder.AppendLine();
    }

    private static void EmitMeterProviderBuilderAddMeterInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "MeterProviderBuilder_" + target.MethodName, index, target.ReceiverType, "builder", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.Append("            var result = ");
        AppendInvocationCall(builder, target, "builder");
        builder.AppendLine(";");
        builder.AppendLine("            var qylMeters = global::Qyl.AutoInstrumentation.QylMetricMeters.GetEnabledMeterNames();");
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

    private static void EmitAzureClientInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "AzureClient_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedAzure.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var result = await client.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(").ConfigureAwait(false);");
        }
        else
        {
            builder.Append("                var result = client.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
        }

        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedAzure.RecordSuccess(activity);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedAzure.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitElasticInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            target.InstrumentationId + "_" + target.MethodName,
            index,
            target.ReceiverType,
            "client",
            target.Parameters,
            target.IsAsync,
            target.TypeParameterList,
            target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedElastic.StartActivity(");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            if (string.Equals(target.ReturnType, "global::System.Threading.Tasks.Task", StringComparison.Ordinal))
            {
                builder.Append("                await client.");
                builder.Append(target.MethodName);
                AppendGenericTypeArgumentList(builder, target.TypeParameterList);
                builder.Append('(');
                AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
                builder.AppendLine(").ConfigureAwait(false);");
                builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedElastic.RecordSuccess(activity);");
            }
            else
            {
                builder.Append("                var result = await client.");
                builder.Append(target.MethodName);
                AppendGenericTypeArgumentList(builder, target.TypeParameterList);
                builder.Append('(');
                AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
                builder.AppendLine(").ConfigureAwait(false);");
                builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedElastic.RecordSuccess(activity);");
                builder.AppendLine("                return result;");
            }
        }
        else
        {
            builder.Append("                var result = client.");
            builder.Append(target.MethodName);
            AppendGenericTypeArgumentList(builder, target.TypeParameterList);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedElastic.RecordSuccess(activity);");
            builder.AppendLine("                return result;");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedElastic.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitWcfClientInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "WcfClient_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.StartActivity(");
        AppendStringLiteral(builder, target.ReceiverType);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            if (string.Equals(target.ReturnType, "global::System.Threading.Tasks.Task", StringComparison.Ordinal))
            {
                builder.Append("                await client.");
                builder.Append(target.MethodName);
                builder.Append('(');
                AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
                builder.AppendLine(").ConfigureAwait(false);");
                builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.RecordSuccess(activity);");
            }
            else
            {
                builder.Append("                var result = await client.");
                builder.Append(target.MethodName);
                builder.Append('(');
                AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
                builder.AppendLine(").ConfigureAwait(false);");
                builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.RecordSuccess(activity);");
                builder.AppendLine("                return result;");
            }
        }
        else if (string.Equals(target.ReturnType, "void", StringComparison.Ordinal))
        {
            builder.Append("                client.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.RecordSuccess(activity);");
        }
        else
        {
            builder.Append("                var result = client.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.RecordSuccess(activity);");
            builder.AppendLine("                return result;");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfClient.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitWcfCoreServiceModelServicesInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "WcfCore_" + target.MethodName, index, target.ReceiverType, "services", target.Parameters, false);
        builder.AppendLine("        {");
        builder.AppendLine("            var result = global::CoreWCF.Configuration.ServiceModelServiceCollectionExtensions.AddServiceModelServices(services);");
        builder.AppendLine("            if (global::Qyl.AutoInstrumentation.QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(global::Qyl.AutoInstrumentation.QylAutoInstrumentationSignal.Traces, global::Qyl.AutoInstrumentation.QylAutoInstrumentationIds.WcfCore))");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Microsoft.Extensions.DependencyInjection.Extensions.ServiceCollectionDescriptorExtensions.TryAddEnumerable(");
        builder.AppendLine("                    result,");
        builder.AppendLine("                    global::Microsoft.Extensions.DependencyInjection.ServiceDescriptor.Singleton<global::CoreWCF.Description.IServiceBehavior, global::Qyl.AutoInstrumentation.Generated.QylCoreWcfServiceBehavior>());");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return result;");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitCoreWcfServiceBehavior(StringBuilder builder)
    {
        builder.AppendLine("    public sealed class QylCoreWcfServiceBehavior : global::CoreWCF.Description.IServiceBehavior");
        builder.AppendLine("    {");
        builder.AppendLine("        public void Validate(global::CoreWCF.Description.ServiceDescription serviceDescription, global::CoreWCF.ServiceHostBase serviceHostBase)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (var endpoint in serviceDescription.Endpoints)");
        builder.AppendLine("            {");
        builder.AppendLine("                foreach (var operation in endpoint.Contract.Operations)");
        builder.AppendLine("                {");
        builder.AppendLine("                    if (HasQylOperationBehavior(operation.OperationBehaviors))");
        builder.AppendLine("                        continue;");
        builder.AppendLine();
        builder.AppendLine("                    operation.OperationBehaviors.Add(new QylCoreWcfOperationBehavior(");
        builder.AppendLine("                        serviceDescription.ServiceType?.FullName ?? serviceDescription.Name,");
        builder.AppendLine("                        endpoint.Contract.Name,");
        builder.AppendLine("                        operation.Name));");
        builder.AppendLine("                }");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void AddBindingParameters(");
        builder.AppendLine("            global::CoreWCF.Description.ServiceDescription serviceDescription,");
        builder.AppendLine("            global::CoreWCF.ServiceHostBase serviceHostBase,");
        builder.AppendLine("            global::System.Collections.ObjectModel.Collection<global::CoreWCF.Description.ServiceEndpoint> endpoints,");
        builder.AppendLine("            global::CoreWCF.Channels.BindingParameterCollection bindingParameters)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ApplyDispatchBehavior(global::CoreWCF.Description.ServiceDescription serviceDescription, global::CoreWCF.ServiceHostBase serviceHostBase)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        private static bool HasQylOperationBehavior(global::System.Collections.IEnumerable behaviors)");
        builder.AppendLine("        {");
        builder.AppendLine("            foreach (var behavior in behaviors)");
        builder.AppendLine("            {");
        builder.AppendLine("                if (behavior is QylCoreWcfOperationBehavior)");
        builder.AppendLine("                    return true;");
        builder.AppendLine("            }");
        builder.AppendLine();
        builder.AppendLine("            return false;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public sealed class QylCoreWcfOperationBehavior : global::CoreWCF.Description.IOperationBehavior");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly string _serviceName;");
        builder.AppendLine("        private readonly string _contractName;");
        builder.AppendLine("        private readonly string _operationName;");
        builder.AppendLine();
        builder.AppendLine("        public QylCoreWcfOperationBehavior(string serviceName, string contractName, string operationName)");
        builder.AppendLine("        {");
        builder.AppendLine("            _serviceName = serviceName;");
        builder.AppendLine("            _contractName = contractName;");
        builder.AppendLine("            _operationName = operationName;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void AddBindingParameters(global::CoreWCF.Description.OperationDescription operationDescription, global::CoreWCF.Channels.BindingParameterCollection bindingParameters)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ApplyClientBehavior(global::CoreWCF.Description.OperationDescription operationDescription, global::CoreWCF.Dispatcher.ClientOperation clientOperation)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void ApplyDispatchBehavior(global::CoreWCF.Description.OperationDescription operationDescription, global::CoreWCF.Dispatcher.DispatchOperation dispatchOperation)");
        builder.AppendLine("        {");
        builder.AppendLine("            if (!global::Qyl.AutoInstrumentation.QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(global::Qyl.AutoInstrumentation.QylAutoInstrumentationSignal.Traces, global::Qyl.AutoInstrumentation.QylAutoInstrumentationIds.WcfCore))");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            if (dispatchOperation.Invoker is null or QylCoreWcfOperationInvoker)");
        builder.AppendLine("                return;");
        builder.AppendLine();
        builder.AppendLine("            dispatchOperation.Invoker = new QylCoreWcfOperationInvoker(dispatchOperation.Invoker, _serviceName, _contractName, _operationName);");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public void Validate(global::CoreWCF.Description.OperationDescription operationDescription)");
        builder.AppendLine("        {");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    public sealed class QylCoreWcfOperationInvoker : global::CoreWCF.Dispatcher.IOperationInvoker");
        builder.AppendLine("    {");
        builder.AppendLine("        private readonly global::CoreWCF.Dispatcher.IOperationInvoker _inner;");
        builder.AppendLine("        private readonly string _serviceName;");
        builder.AppendLine("        private readonly string _contractName;");
        builder.AppendLine("        private readonly string _operationName;");
        builder.AppendLine();
        builder.AppendLine("        public QylCoreWcfOperationInvoker(global::CoreWCF.Dispatcher.IOperationInvoker inner, string serviceName, string contractName, string operationName)");
        builder.AppendLine("        {");
        builder.AppendLine("            _inner = inner;");
        builder.AppendLine("            _serviceName = serviceName;");
        builder.AppendLine("            _contractName = contractName;");
        builder.AppendLine("            _operationName = operationName;");
        builder.AppendLine("        }");
        builder.AppendLine();
        builder.AppendLine("        public object[] AllocateInputs() => _inner.AllocateInputs();");
        builder.AppendLine();
        builder.AppendLine("        public async global::System.Threading.Tasks.ValueTask<(object returnValue, object[] outputs)> InvokeAsync(object instance, object[] inputs)");
        builder.AppendLine("        {");
        builder.AppendLine("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedWcfCore.StartActivity(_serviceName, _contractName, _operationName);");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.AppendLine("                var result = await _inner.InvokeAsync(instance, inputs).ConfigureAwait(false);");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfCore.RecordSuccess(activity);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedWcfCore.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientAsyncUnaryInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "GrpcNetClientAsyncUnary_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.StartActivity(");
        AppendStringLiteral(builder, target.ReceiverType);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.Append(", ");
        AppendGrpcMetadataExpression(builder, target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var call = client.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("                var observedResponseHeaders = global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.ObserveResponseHeadersAsync(call.ResponseHeadersAsync, activity);");
        builder.Append("                return new ");
        builder.Append(target.ReturnType);
        builder.AppendLine("(");
        builder.AppendLine("                    global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
        builder.AppendLine("                    () =>");
        builder.AppendLine("                    {");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            call.Dispose();");
        builder.AppendLine("                        }");
        builder.AppendLine("                        finally");
        builder.AppendLine("                        {");
        builder.AppendLine("                            global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.Dispose(activity);");
        builder.AppendLine("                        }");
        builder.AppendLine("                    });");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientAsyncServerStreamingInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "GrpcNetClientAsyncServerStreaming_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        EmitGrpcCallPreamble(builder, target);
        builder.Append("                return new ");
        builder.Append(target.ReturnType);
        builder.AppendLine("(");
        builder.AppendLine("                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
        EmitGrpcDisposeAction(builder);
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientAsyncClientStreamingInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "GrpcNetClientAsyncClientStreaming_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        EmitGrpcCallPreamble(builder, target);
        builder.Append("                return new ");
        builder.Append(target.ReturnType);
        builder.AppendLine("(");
        builder.AppendLine("                    call.RequestStream,");
        builder.AppendLine("                    global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.ObserveUnaryResponseAsync(call.ResponseAsync, call.ResponseHeadersAsync, activity),");
        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
        EmitGrpcDisposeAction(builder);
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcNetClientAsyncDuplexStreamingInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "GrpcNetClientAsyncDuplexStreaming_" + target.MethodName, index, target.ReceiverType, "client", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        EmitGrpcCallPreamble(builder, target);
        builder.Append("                return new ");
        builder.Append(target.ReturnType);
        builder.AppendLine("(");
        builder.AppendLine("                    call.RequestStream,");
        builder.AppendLine("                    QylObservedAsyncStreamReader.Create(call.ResponseStream, activity, call.ResponseHeadersAsync),");
        builder.AppendLine("                    observedResponseHeaders,");
        builder.AppendLine("                    call.GetStatus,");
        builder.AppendLine("                    call.GetTrailers,");
        EmitGrpcDisposeAction(builder);
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGrpcCallPreamble(StringBuilder builder, InterceptorTarget target)
    {
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.StartActivity(");
        AppendStringLiteral(builder, target.ReceiverType);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
        builder.Append(", ");
        AppendGrpcMetadataExpression(builder, target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var call = client.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("                var observedResponseHeaders = global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.ObserveResponseHeadersAsync(call.ResponseHeadersAsync, activity);");
    }

    private static void EmitGrpcDisposeAction(StringBuilder builder)
    {
        builder.AppendLine("                    () =>");
        builder.AppendLine("                    {");
        builder.AppendLine("                        try");
        builder.AppendLine("                        {");
        builder.AppendLine("                            call.Dispose();");
        builder.AppendLine("                        }");
        builder.AppendLine("                        finally");
        builder.AppendLine("                        {");
        builder.AppendLine("                            global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.Dispose(activity);");
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

    private static void EmitGrpcStreamReaderWrapper(StringBuilder builder)
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
        builder.AppendLine("                    global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.CaptureCompletedResponseHeaders(_responseHeadersTask, _activity);");
        builder.AppendLine("                    global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordStreamingComplete(_activity);");
        builder.AppendLine("                }");
        builder.AppendLine();
        builder.AppendLine("                return hasNext;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.RecordException(_activity, exception);");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGrpcNetClient.Dispose(_activity);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
    }

    private static void EmitKafkaProducerInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "KafkaProducer_" + target.MethodName, index, target.ReceiverType, "producer", target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedKafka.StartProducerActivity(");
        AppendKafkaTopicExpression(builder, target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var result = await producer.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(").ConfigureAwait(false);");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedKafka.RecordSuccess(activity);");
            builder.AppendLine("                return result;");
        }
        else
        {
            builder.Append("                producer.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedKafka.RecordSuccess(activity);");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedKafka.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitKafkaConsumerInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "KafkaConsumer_" + target.MethodName, index, target.ReceiverType, "consumer", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.AppendLine("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedKafka.StartConsumerActivity();");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var result = consumer.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedKafka.RecordConsumeSuccess(activity, result is null ? null : result.Topic);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedKafka.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitMassTransitInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            "MassTransit_" + target.MethodName,
            index,
            target.ReceiverType,
            "endpoint",
            target.Parameters,
            isAsync: true,
            typeParameterList: target.TypeParameterList,
            constraintClauses: target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedMassTransit.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                await ");
        AppendInvocationCall(builder, target, "endpoint");
        builder.AppendLine(".ConfigureAwait(false);");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedMassTransit.RecordSuccess(activity);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedMassTransit.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitNServiceBusInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            "NServiceBus_" + target.MethodName,
            index,
            target.ReceiverType,
            "endpoint",
            target.Parameters,
            isAsync: true,
            typeParameterList: target.TypeParameterList,
            constraintClauses: target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.AppendLine("            var metricStart = global::Qyl.AutoInstrumentation.QylNServiceBusMetrics.GetTimestamp();");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedNServiceBus.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                await endpoint.");
        builder.Append(target.MethodName);
        AppendGenericTypeArgumentList(builder, target.TypeParameterList);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(").ConfigureAwait(false);");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedNServiceBus.RecordSuccess(activity);");
        builder.Append("                global::Qyl.AutoInstrumentation.QylNServiceBusMetrics.RecordDuration(metricStart, ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedNServiceBus.RecordException(activity, exception);");
        builder.Append("                global::Qyl.AutoInstrumentation.QylNServiceBusMetrics.RecordDuration(metricStart, ");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitQuartzInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "Quartz_" + target.MethodName, index, target.ReceiverType, "job", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.AppendLine("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedQuartz.StartActivity();");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var resultTask = job.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("                return global::Qyl.AutoInstrumentation.QylInterceptedQuartz.ObserveAsync(resultTask, activity);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedQuartz.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
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

    private static void EmitStackExchangeRedisInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "StackExchangeRedis_" + target.MethodName, index, target.ReceiverType, "database", target.Parameters, isAsync: true);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedRedis.StartCommandActivity(");
        AppendStringLiteral(builder, GetRedisOperationName(target.MethodName));
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var result = await database.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(").ConfigureAwait(false);");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedRedis.RecordSuccess(activity);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedRedis.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitGraphQlInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "GraphQl_" + target.MethodName, index, target.ReceiverType, "executer", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.AppendLine("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedGraphQl.StartActivity();");
        builder.AppendLine("            if (activity is not null)");
        builder.AppendLine("            {");
        builder.Append("                global::Qyl.AutoInstrumentation.QylInterceptedGraphQl.RecordExecutionOptions(activity, ");
        AppendGraphQlOperationNameExpression(builder, target);
        builder.Append(", ");
        AppendGraphQlDocumentCaptureExpression(builder, target);
        builder.AppendLine(");");
        builder.AppendLine("            }");
        builder.AppendLine("            try");
        builder.AppendLine("            {");
        builder.Append("                var resultTask = executer.");
        builder.Append(target.MethodName);
        builder.Append('(');
        AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
        builder.AppendLine(");");
        builder.AppendLine("                return global::Qyl.AutoInstrumentation.QylInterceptedGraphQl.ObserveAsync(resultTask, activity);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedGraphQl.RecordException(activity, exception);");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
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
            builder.Append(" is null ? null : ");
            builder.Append(target.Parameters[0].Name);
            builder.Append(".OperationName");
            return;
        }

        builder.Append("null");
    }

    private static void EmitMongoDbInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            "MongoDb_" + target.MethodName,
            index,
            target.ReceiverType,
            "collection",
            target.Parameters,
            isAsync: false,
            typeParameterList: target.TypeParameterList,
            constraintClauses: target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (string.Equals(target.ReturnType, "global::System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            builder.Append("                var resultTask = ");
            AppendInvocationCall(builder, target, "collection");
            builder.AppendLine(";");
            builder.AppendLine("                return global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.ObserveAsync(resultTask, activity);");
        }
        else if (target.IsAsync)
        {
            builder.Append("                var resultTask = ");
            AppendInvocationCall(builder, target, "collection");
            builder.AppendLine(";");
            builder.AppendLine("                return global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.ObserveAsync(resultTask, activity);");
        }
        else if (string.Equals(target.ReturnType, "void", StringComparison.Ordinal))
        {
            builder.Append("                ");
            AppendInvocationCall(builder, target, "collection");
            builder.AppendLine(";");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.RecordSuccess(activity);");
        }
        else
        {
            builder.Append("                var result = ");
            AppendInvocationCall(builder, target, "collection");
            builder.AppendLine(";");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.RecordSuccess(activity);");
            builder.AppendLine("                return result;");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedMongoDb.RecordException(activity, exception);");
        if (target.IsAsync)
            builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        if (!target.IsAsync)
        {
            builder.AppendLine("            finally");
            builder.AppendLine("            {");
            builder.AppendLine("                activity?.Dispose();");
            builder.AppendLine("            }");
        }
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitRabbitMqInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            "RabbitMq_" + target.MethodName,
            index,
            target.ReceiverType,
            "channel",
            target.Parameters,
            target.IsAsync,
            target.TypeParameterList,
            target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedRabbitMq.StartPublishActivity(");
        AppendRabbitMqExchangeExpression(builder, target);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                await ");
            AppendInvocationCall(builder, target, "channel");
            builder.AppendLine(".ConfigureAwait(false);");
        }
        else
        {
            builder.Append("                ");
            AppendInvocationCall(builder, target, "channel");
            builder.AppendLine(";");
        }

        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedRabbitMq.RecordSuccess(activity);");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedRabbitMq.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
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

    private static void EmitLoggerInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var attribute = Microsoft.CodeAnalysis.CSharp.CSharpExtensions.GetInterceptsLocationAttributeSyntax(invocation.Location);
        var displayLocation = invocation.Location.GetDisplayLocation();
        builder.Append("        // Intercepted call at ");
        builder.AppendLine(displayLocation);
        builder.Append("        ");
        builder.AppendLine(attribute);
        builder.Append("        public static void ILogger_Log_");
        builder.Append(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
        builder.AppendLine("<TState>(");
        builder.AppendLine("            this global::Microsoft.Extensions.Logging.ILogger logger,");
        builder.AppendLine("            global::Microsoft.Extensions.Logging.LogLevel logLevel,");
        builder.AppendLine("            global::Microsoft.Extensions.Logging.EventId eventId,");
        builder.AppendLine("            TState state,");
        builder.AppendLine("            global::System.Exception? exception,");
        builder.AppendLine("            global::System.Func<TState, global::System.Exception?, string> formatter)");
        builder.AppendLine("            => global::Qyl.AutoInstrumentation.QylInterceptedLogger.Log(logger, logLevel, eventId, state, exception, formatter);");
        builder.AppendLine();
    }

    private static void EmitLoggerExtensionInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, "void", "LoggerExtensions_" + target.MethodName, index, target.ReceiverType, "logger", target.Parameters, isAsync: false);
        builder.Append("            => global::Qyl.AutoInstrumentation.QylInterceptedLogger.LogExtension(logger, ");
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

    private static void EmitExternalLoggerInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index, string domain)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, "void", target.InstrumentationId + "_" + target.MethodName, index, target.ReceiverType, "logger", target.Parameters, isAsync: false);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedExternalLogger.StartActivity(");
        AppendStringLiteral(builder, target.InstrumentationId);
        builder.Append(", ");
        AppendStringLiteral(builder, domain);
        builder.Append(", ");
        AppendStringLiteral(builder, target.MethodName);
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
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedExternalLogger.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
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

    private static void EmitEntityFrameworkCoreDbContextInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(builder, invocation.Location, target.ReturnType, "EntityFrameworkCoreDbContext_" + target.MethodName, index, target.ReceiverType, "dbContext", target.Parameters, target.IsAsync);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (target.IsAsync)
        {
            builder.Append("                var result = await dbContext.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(").ConfigureAwait(false);");
        }
        else
        {
            builder.Append("                var result = dbContext.");
            builder.Append(target.MethodName);
            builder.Append('(');
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: false);
            builder.AppendLine(");");
        }

        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.RecordSuccess(activity);");
        builder.AppendLine("                return result;");
        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitEntityFrameworkCoreQueryableInterceptor(StringBuilder builder, InterceptedInvocation invocation, int index)
    {
        var target = invocation.Target;
        EmitAttributeAndSignature(
            builder,
            invocation.Location,
            target.ReturnType,
            "EntityFrameworkCoreQueryable_" + target.MethodName,
            index,
            target.ReceiverType,
            "query",
            target.Parameters,
            isAsync: true,
            target.TypeParameterList,
            target.ConstraintClauses);
        builder.AppendLine("        {");
        builder.Append("            var activity = global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.StartActivity(");
        AppendStringLiteral(builder, target.MethodName);
        builder.AppendLine(");");
        builder.AppendLine("            try");
        builder.AppendLine("            {");

        if (string.Equals(target.ReturnType, "global::System.Threading.Tasks.Task", StringComparison.Ordinal))
        {
            builder.Append("                await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.");
            builder.Append(target.MethodName);
            AppendGenericTypeArgumentList(builder, target.TypeParameterList);
            builder.Append("(query");
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
            builder.AppendLine(").ConfigureAwait(false);");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.RecordSuccess(activity);");
        }
        else
        {
            builder.Append("                var result = await global::Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.");
            builder.Append(target.MethodName);
            AppendGenericTypeArgumentList(builder, target.TypeParameterList);
            builder.Append("(query");
            AppendArgumentList(builder, target.Parameters, includeLeadingComma: true);
            builder.AppendLine(").ConfigureAwait(false);");
            builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.RecordSuccess(activity);");
            builder.AppendLine("                return result;");
        }

        builder.AppendLine("            }");
        builder.AppendLine("            catch (global::System.Exception exception)");
        builder.AppendLine("            {");
        builder.AppendLine("                global::Qyl.AutoInstrumentation.QylInterceptedEntityFrameworkCore.RecordException(activity, exception);");
        builder.AppendLine("                throw;");
        builder.AppendLine("            }");
        builder.AppendLine("            finally");
        builder.AppendLine("            {");
        builder.AppendLine("                activity?.Dispose();");
        builder.AppendLine("            }");
        builder.AppendLine("        }");
        builder.AppendLine();
    }

    private static void EmitAttributeAndSignature(
        StringBuilder builder,
        InterceptableLocation location,
        string returnType,
        string methodPrefix,
        int index,
        string receiverType,
        string receiverName,
        ImmutableArray<ParameterSpec> parameters,
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
        ImmutableArray<ParameterSpec> parameters,
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

    private static void AppendParameterList(StringBuilder builder, ImmutableArray<ParameterSpec> parameters)
    {
        foreach (var parameter in parameters)
        {
            builder.Append(", ");
            if (parameter.IsParams)
                builder.Append("params ");

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

    private static void AppendArgumentList(StringBuilder builder, ImmutableArray<ParameterSpec> parameters, bool includeLeadingComma)
    {
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0 || includeLeadingComma)
                builder.Append(", ");

            builder.Append(parameters[i].Name);
        }
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
        var isAsync = methodName.EndsWith("Async", StringComparison.Ordinal);
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
            isAsync);
        return true;
    }

    private static bool TryGetDbCommandInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (!InheritsFromOrIs(symbol.ContainingType, "global::System.Data.Common.DbCommand"))
            return false;

        var methodName = symbol.Name;
        var isAsync = methodName.EndsWith("Async", StringComparison.Ordinal);
        if (!TryGetDbCommandParameters(symbol, methodName, out var parameters))
            return false;

        if (!TryGetDbCommandReturn(symbol, methodName, isAsync, out var returnType))
            return false;

        var instrumentationId = GetDbInstrumentationId(symbol.ContainingType);
        target = new InterceptorTarget(
            InterceptorKind.DbCommand,
            "signals.traces." + instrumentationId,
            instrumentationId,
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            isAsync);
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
            ExtensionContainingType: extensionContainingType);
        return true;
    }

    private static bool TryGetAzureClientInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        if (symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
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
            !named.Name.EndsWith("Client", StringComparison.Ordinal))
        {
            return false;
        }

        var namespaceName = named.ContainingNamespace.ToDisplayString();
        return namespaceName.StartsWith("Azure.", StringComparison.Ordinal) &&
               !namespaceName.StartsWith("Azure.Core", StringComparison.Ordinal);
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
            !CanEmitByValueParameters(symbol) ||
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
        if (!IsSupportedElasticTransportMethod(symbol.Name) ||
            symbol.IsStatic ||
            symbol.MethodKind is not MethodKind.Ordinary ||
            symbol.ReturnsVoid ||
            !CanEmitByValueParameters(symbol) ||
            !IsOrImplementsType(symbol.ContainingType, "Elastic.Transport", "ITransport") ||
            !CanEmitElasticReturn(symbol.ReturnType, out var isAsync))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.ElasticTransport,
            "signals.traces.ELASTICTRANSPORT",
            "ELASTICTRANSPORT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
            isAsync,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol));
        return true;
    }

    private static bool IsElasticsearchClientType(ITypeSymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol named ||
            !named.Name.EndsWith("Client", StringComparison.Ordinal))
        {
            return false;
        }

        return named.ContainingNamespace.ToDisplayString().StartsWith("Elastic.Clients.Elasticsearch", StringComparison.Ordinal);
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
            !CanEmitByValueParameters(symbol) ||
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
           named.ContainingNamespace.ToDisplayString().StartsWith("System.ServiceModel", StringComparison.Ordinal);

    private static bool TryGetWcfCoreServiceModelServicesInvocation(IMethodSymbol symbol, out InterceptorTarget target)
    {
        target = default;
        var original = symbol.ReducedFrom ?? symbol;
        var hasReceiverParameter = symbol.Parameters.Length is 1 &&
            IsType(symbol.Parameters[0].Type, "global::Microsoft.Extensions.DependencyInjection.IServiceCollection");

        if ((symbol.ReducedFrom is null && !hasReceiverParameter) ||
            !string.Equals(symbol.Name, "AddServiceModelServices", StringComparison.Ordinal) ||
            !IsType(original.ContainingType, "global::CoreWCF.Configuration.ServiceModelServiceCollectionExtensions") ||
            !IsType(symbol.ReturnType, "global::Microsoft.Extensions.DependencyInjection.IServiceCollection") ||
            (symbol.Parameters.Length is not 0 && !hasReceiverParameter))
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.WcfCoreServiceModelServices,
            "signals.traces.WCFCORE",
            "WCFCORE",
            "global::Microsoft.Extensions.DependencyInjection.IServiceCollection",
            "AddServiceModelServices",
            CleanTypeName(symbol.ReturnType, symbol),
            ImmutableArray<ParameterSpec>.Empty,
            false);
        return true;
    }

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
        if (!IsSupportedMassTransitOperation(symbol.Name) ||
            !IsTask(symbol.ReturnType) ||
            !IsMassTransitEndpointType(symbol.ContainingType) ||
            symbol.Parameters.Length is 0)
        {
            return false;
        }

        target = new InterceptorTarget(
            InterceptorKind.MassTransitMessageOperation,
            "signals.traces.MASSTRANSIT",
            "MASSTRANSIT",
            CleanTypeName(symbol.ContainingType),
            symbol.Name,
            CleanTypeName(symbol.ReturnType, symbol),
            Parameters(symbol),
            true,
            GetTypeParameterList(symbol),
            GetConstraintClauses(symbol));
        return true;
    }

    private static bool IsSupportedMassTransitOperation(string methodName)
        => methodName is "Publish" or "Send";

    private static bool IsMassTransitEndpointType(ITypeSymbol? symbol)
        => IsOrImplementsType(symbol, "MassTransit", "IPublishEndpoint") ||
           IsOrImplementsType(symbol, "MassTransit", "ISendEndpoint");

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
            GetReducedExtensionContainingType(symbol));
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
            false);
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
            false);
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

    private static bool IsLog4NetLoggerType(ITypeSymbol? symbol)
    {
        if (symbol is not INamedTypeSymbol named)
            return false;

        if (IsTypeByMetadata(named, "log4net", "ILog"))
            return true;

        foreach (var interfaceType in named.AllInterfaces)
        {
            if (IsTypeByMetadata(interfaceType, "log4net", "ILog"))
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
            !CanEmitByValueParameters(symbol) ||
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
            GetConstraintClauses(symbol));
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

    private static bool TryGetDbCommandParameters(IMethodSymbol symbol, string methodName, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static InterceptorTarget HttpTarget(IMethodSymbol symbol, string methodName, string returnType, ImmutableArray<ParameterSpec> parameters)
        => new(
            InterceptorKind.HttpClient,
            "signals.traces.HTTPCLIENT",
            "HTTPCLIENT",
            CleanTypeName(symbol.ContainingType),
            methodName,
            returnType,
            parameters,
            false);

    private static bool TryGetSendShape(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetRequestUriShape(IMethodSymbol symbol, bool allowCompletionOption, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetRequestUriContentShape(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetNoParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = ImmutableArray<ParameterSpec>.Empty;
            return true;
        }

        parameters = ImmutableArray<ParameterSpec>.Empty;
        return false;
    }

    private static bool TryGetOptionalCancellationTokenParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = ImmutableArray<ParameterSpec>.Empty;
            return true;
        }

        if (symbol.Parameters.Length is 1 && IsType(symbol.Parameters[0].Type, "global::System.Threading.CancellationToken"))
        {
            parameters = Parameters(symbol);
            return true;
        }

        parameters = ImmutableArray<ParameterSpec>.Empty;
        return false;
    }

    private static bool TryGetDbExecuteReaderParameters(IMethodSymbol symbol, bool allowCancellationToken, out ImmutableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = ImmutableArray<ParameterSpec>.Empty;
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

        parameters = ImmutableArray<ParameterSpec>.Empty;
        return false;
    }

    private static bool TryGetKafkaProduceParameters(IMethodSymbol symbol, bool isAsync, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetKafkaConsumeParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetRedisCommandParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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
            if (parameter.RefKind is not RefKind.None)
                return false;
        }

        return true;
    }

    private static bool TryGetRedisStringGetParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool TryGetEfCoreSaveChangesParameters(IMethodSymbol symbol, bool allowCancellationToken, out ImmutableArray<ParameterSpec> parameters)
    {
        if (symbol.Parameters.Length is 0)
        {
            parameters = ImmutableArray<ParameterSpec>.Empty;
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

        parameters = ImmutableArray<ParameterSpec>.Empty;
        return false;
    }

    private static bool TryGetRabbitMqBasicPublishParameters(IMethodSymbol symbol, out ImmutableArray<ParameterSpec> parameters)
    {
        parameters = ImmutableArray<ParameterSpec>.Empty;
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

    private static bool CanEmitByValueParameters(IMethodSymbol symbol)
    {
        foreach (var parameter in symbol.Parameters)
        {
            if (parameter.RefKind is not RefKind.None)
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
        ImmutableArray<ParameterSpec> parameters)
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

    private static ImmutableArray<ParameterSpec> Parameters(IMethodSymbol symbol)
    {
        var builder = ImmutableArray.CreateBuilder<ParameterSpec>(symbol.Parameters.Length);
        for (var i = 0; i < symbol.Parameters.Length; i++)
            builder.Add(new ParameterSpec(
                CleanTypeName(symbol.Parameters[i].Type, symbol),
                "p" + i.ToString(System.Globalization.CultureInfo.InvariantCulture),
                GetDefaultValueExpression(symbol.Parameters[i]),
                symbol.Parameters[i].IsParams));

        return builder.ToImmutable();
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
        if (display.StartsWith("global::Microsoft.Data.SqlClient.", StringComparison.Ordinal) ||
            display.StartsWith("global::System.Data.SqlClient.", StringComparison.Ordinal))
        {
            return "SQLCLIENT";
        }

        if (display.StartsWith("global::Microsoft.Data.Sqlite.", StringComparison.Ordinal))
            return "SQLITE";

        if (display.StartsWith("global::Npgsql.", StringComparison.Ordinal))
            return "NPGSQL";

        if (display.StartsWith("global::MySqlConnector.", StringComparison.Ordinal))
            return "MYSQLCONNECTOR";

        if (display.StartsWith("global::MySql.Data.", StringComparison.Ordinal))
            return "MYSQLDATA";

        if (display.StartsWith("global::Oracle.ManagedDataAccess.", StringComparison.Ordinal))
            return "ORACLEMDA";

        return "ADONET";
    }

    private static string CleanTypeName(ITypeSymbol symbol)
        => symbol.ToDisplayString(FullyQualifiedFormat);

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
            var constructedName = named.ConstructedFrom.ToDisplayString(FullyQualifiedFormat);
            var genericStart = constructedName.IndexOf('<');
            var typeName = genericStart < 0 ? constructedName : constructedName.Substring(0, genericStart);
            var arguments = named.TypeArguments
                .Select(typeArgument => CleanTypeName(typeArgument, typeParameters, typeArguments));
            return typeName + "<" + string.Join(", ", arguments) + ">";
        }

        return CleanTypeName(symbol);
    }

    private static bool RequiresGrpcStreamReader(InterceptedInvocation[] invocations)
        => invocations.Any(static invocation =>
            invocation.Target.Kind is InterceptorKind.GrpcNetClientAsyncServerStreamingCall or
                InterceptorKind.GrpcNetClientAsyncDuplexStreamingCall);

    private static bool RequiresCoreWcfServiceBehavior(InterceptedInvocation[] invocations)
        => invocations.Any(static invocation => invocation.Target.Kind is InterceptorKind.WcfCoreServiceModelServices);

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
        WcfCoreServiceModelServices,
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

    private readonly record struct ParameterSpec(string TypeName, string Name, string DefaultValueExpression = "", bool IsParams = false);

    private readonly record struct InterceptorTarget(
        InterceptorKind Kind,
        string ContractKey,
        string InstrumentationId,
        string ReceiverType,
        string MethodName,
        string ReturnType,
        ImmutableArray<ParameterSpec> Parameters,
        bool IsAsync,
        string TypeParameterList = "",
        string ConstraintClauses = "",
        string ExtensionContainingType = "");

    private readonly record struct InterceptedInvocation(InterceptorTarget Target, InterceptableLocation Location);
}
