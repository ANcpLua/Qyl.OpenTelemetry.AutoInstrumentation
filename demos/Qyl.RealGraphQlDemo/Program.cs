using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL;
using GraphQL.MicrosoftDI;
using GraphQL.Resolvers;
using GraphQL.Types;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;
using GraphqlAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes;
using QylAttributes = Qyl.Telemetry.SemanticConventions.Incubating.Attributes.Qyl.QylAttributes;
using QylTelemetryNames = Qyl.Telemetry.SemanticConventions.Names.QylTelemetryNames;

// The real registration path: AddQyl subscribes to GraphQL.NET's own ActivitySource and installs
// the one native-span processor. GraphQL.NET is the third-bucket case -- that source stays SILENT
// until the application calls UseTelemetry() on its own IGraphQLBuilder, and qyl never calls the
// opt-in on the application's behalf.
var exportedActivities = new List<Activity>();
var builder = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "Qyl.RealGraphQlDemo",
    DisableDefaults = true,
});
builder.AddQyl(options =>
{
    options.ServiceName = "qyl-real-graphql-demo";
    // The live-check gate points this at its OTLP listener. Unset, the demo exports into a
    // closed port and asserts on its in-memory exporter alone.
    options.CollectorEndpoint =
        new Uri(Environment.GetEnvironmentVariable("QYL_LIVE_CHECK_ENDPOINT") ?? "http://127.0.0.1:1");
    options.EnableCollectorDiscovery = false;
    options.EnableLogExport = false;
    options.EnableMetricsExport = false;
    options.EnableSessionPropagation = false;
});
builder.Services.AddOpenTelemetry()
    .WithTracing(tracing => tracing.AddInMemoryExporter(exportedActivities));

using var host = builder.Build();
await host.StartAsync();

// Two executers over one schema, identical but for the single opt-in call. graphql.document is
// GraphQL.NET's own UseTelemetry option, not a qyl setting.
await using var withTelemetry = new ServiceCollection()
    .AddGraphQL(graphql => graphql.UseTelemetry(telemetry => telemetry.RecordDocument = true))
    .BuildServiceProvider();
await using var withoutTelemetry = new ServiceCollection()
    .AddGraphQL(static graphql => graphql.ConfigureExecutionOptions(static _ => { }))
    .BuildServiceProvider();

var schema = new Schema(GraphQlServices.Instance) { Query = new ProbeQuery() };

var capturedSpans = new List<CapturedSpan>();
var capturedLock = new Lock();
var operation = GraphQlReport.WithTelemetryOperation;

// Listen to EVERY ActivitySource in the process, not only the ones qyl subscribes, so the report
// can assert the EXACT set of (scope, kind) tuples each operation produces rather than a filtered
// subset. Sources whose name starts with "Experimental." are the single exclusion: they are the
// runtime's own socket / DNS / TLS / connection-pool diagnostics, which qyl never subscribes and
// which appear here only because this listener listens to everything. That prefix is the ONLY
// exclusion -- any other third source shows up in the asserted multiset and fails the gate.
using var listener = new ActivityListener
{
    ShouldListenTo = static _ => true,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity =>
    {
        lock (capturedLock)
        {
            capturedSpans.Add(new CapturedSpan(operation, activity.Source.Name, activity.Kind.ToString()));
        }
    },
};

ActivitySource.AddActivityListener(listener);

await ExecuteAsync(withTelemetry, schema, "graphql-with-telemetry");

// The with/without proof: the same query on the same schema through a pipeline that never called
// UseTelemetry() must produce ZERO spans from the GraphQL source. A silent library cannot pass
// this gate on qyl's AddSource alone.
operation = GraphQlReport.WithoutTelemetryOperation;
await ExecuteAsync(withoutTelemetry, schema, "graphql-without-telemetry");

listener.Dispose();

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();

var report = GraphQlReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    capturedSpans.ToArray(),
    exportedActivities.Select(CapturedActivity.From).ToArray());

var json = JsonSerializer.Serialize(report, RealGraphQlJsonContext.Default.GraphQlReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task ExecuteAsync(IServiceProvider services, ISchema schema, string token)
{
    var result = await services.GetRequiredService<IDocumentExecuter>().ExecuteAsync(new ExecutionOptions
    {
        Schema = schema,
        Query = GraphQlReport.QueryText,
        OperationName = GraphQlReport.OperationName,
        RequestServices = services,
    });

    if (result.Errors is { Count: > 0 })
        Console.WriteLine($"{token}-errors=" + result.Errors.Count.ToString(CultureInfo.InvariantCulture));
    else
        Console.WriteLine($"{token}-success=true");
}

internal sealed class ProbeQuery : ObjectGraphType
{
    public ProbeQuery()
    {
        AddField(new FieldType
        {
            Name = "hello",
            ResolvedType = new StringGraphType(),
            Resolver = new FuncFieldResolver<string>(static _ => "world"),
        });
    }
}

internal sealed class GraphQlServices : IServiceProvider
{
    public static GraphQlServices Instance { get; } = new();

    public object? GetService(Type serviceType)
        => serviceType == typeof(ProbeQuery) ? new ProbeQuery() : null;
}

/// <summary>One <c>(scope, kind)</c> tuple the raw listener saw, tagged with the operation it fell in.</summary>
internal sealed record CapturedSpan(string Operation, string Scope, string Kind)
{
    public string Tuple => Scope + "/" + Kind;
}

internal sealed record OperationSpanSet(string Operation, string[] Expected, string[] Actual);

internal sealed record CapturedActivity(
    string Source,
    string Name,
    string OperationName,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.Source.Name,
            activity.DisplayName,
            activity.OperationName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record GraphQlReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    OperationSpanSet[] SpanSets,
    string[] ExcludedScopes,
    CapturedActivity[] Activities)
{
    internal const string OperationName = "QylHello";
    internal const string QueryText = "query QylHello { hello }";
    internal const string WithTelemetryOperation = "query-with-telemetry";
    internal const string WithoutTelemetryOperation = "query-without-telemetry";

    // The runtime's own socket / DNS / TLS / connection-pool diagnostics. This is the ONE prefix the
    // span-set assertion excludes; everything else the process emits has to be an expected tuple.
    private const string ExcludedScopePrefix = "Experimental.";

    private const string QylScope = "Qyl.Telemetry.AutoInstrumentation";

    private const string GraphQlActivityName = "graphql";

    public static GraphQlReport Create(
        string runtimeMode,
        CapturedSpan[] capturedSpans,
        CapturedActivity[] activities)
    {
        var failures = new List<string>();

        var excludedScopes = capturedSpans
            .Where(static span => span.Scope.StartsWith(ExcludedScopePrefix, StringComparison.Ordinal))
            .Select(static span => span.Scope)
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToArray();

        // One executed operation produces exactly one non-Experimental span -- GraphQL.NET's own --
        // and only when the application opted in. The deleted qyl interceptor span reappearing, or
        // any other library joining in, changes these multisets and fails the gate.
        var spanSets = new[]
        {
            SpanSet(
                capturedSpans,
                WithTelemetryOperation,
                [QylTelemetryNames.VendorActivitySources.GraphQL + "/Internal"],
                failures),
            SpanSet(capturedSpans, WithoutTelemetryOperation, [], failures),
        };

        // Stated separately from the multiset so the deleted qyl span is named in the failure.
        var qylScopeSpans = capturedSpans.Count(static span => StringComparer.Ordinal.Equals(span.Scope, QylScope));
        if (qylScopeSpans is not 0)
            failures.Add($"expected 0 spans from the {QylScope} scope, got {qylScopeSpans.ToString(CultureInfo.InvariantCulture)}");

        var graphQlSpans = activities
            .Where(static activity => StringComparer.Ordinal.Equals(
                activity.Source,
                QylTelemetryNames.VendorActivitySources.GraphQL))
            .ToArray();

        if (graphQlSpans.Length is not 1)
            failures.Add($"expected exactly 1 exported GraphQL span, got {graphQlSpans.Length.ToString(CultureInfo.InvariantCulture)}");

        foreach (var span in graphQlSpans)
        {
            // GraphQL.NET's own attributes plus the one tag the qyl processor owns. The key set is
            // asserted exactly: a key the library stops writing, or one qyl starts writing, is a
            // contract change and must fail here.
            RequireExactTagKeys(
                span,
                [
                    GraphqlAttributes.OperationName,
                    GraphqlAttributes.OperationType,
                    GraphqlAttributes.Document,
                    QylAttributes.InstrumentationDomain,
                ],
                failures);
            RequireTag(span, GraphqlAttributes.OperationName, OperationName, failures);
            RequireTag(span, GraphqlAttributes.OperationType, GraphqlAttributes.OperationTypeValues.Query, failures);
            // Written because this demo's own UseTelemetry(o => o.RecordDocument = true) asked for
            // it. qyl has no say in it.
            RequireTag(span, GraphqlAttributes.Document, QueryText, failures);

            // The attribute the qyl processor owns: without it the collector cannot classify the
            // span, because it classifies on attribute presence and never on the span name.
            RequireTag(
                span,
                QylAttributes.InstrumentationDomain,
                QylAttributes.InstrumentationDomainValues.GraphQl,
                failures);

            var qylTags = span.Tags.Keys.Count(static key => key.StartsWith("qyl.", StringComparison.Ordinal));
            if (qylTags is not 1)
                failures.Add($"expected exactly 1 qyl.* tag on {span.Name}, got {qylTags.ToString(CultureInfo.InvariantCulture)}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");

            // GraphQL.NET starts one activity named "graphql" and renames it to the operation type
            // and name once the document is parsed. Neither carries the document text.
            if (!StringComparer.Ordinal.Equals(span.OperationName, GraphQlActivityName))
                failures.Add($"unexpected operation name: {span.OperationName}");

            var expectedDisplayName = GraphqlAttributes.OperationTypeValues.Query + " " + OperationName;
            if (!StringComparer.Ordinal.Equals(span.Name, expectedDisplayName))
                failures.Add($"expected span name {expectedDisplayName}, got {span.Name}");

            // A GraphQL request that returns no errors leaves the status unset; GraphQL.NET sets
            // Error only when an exception escapes the execution pipeline.
            if (!StringComparer.Ordinal.Equals(span.Status, "Unset"))
                failures.Add($"expected span status Unset, got {span.Status}");
        }

        return new GraphQlReport(
            runtimeMode,
            failures.Count is 0,
            failures.ToArray(),
            spanSets,
            excludedScopes,
            graphQlSpans);
    }

    private static OperationSpanSet SpanSet(
        CapturedSpan[] capturedSpans,
        string operation,
        string[] expected,
        ICollection<string> failures)
    {
        var actual = capturedSpans
            .Where(span => StringComparer.Ordinal.Equals(span.Operation, operation))
            .Where(static span => !span.Scope.StartsWith(ExcludedScopePrefix, StringComparison.Ordinal))
            .Select(static span => span.Tuple)
            .Order(StringComparer.Ordinal)
            .ToArray();

        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(sortedExpected, StringComparer.Ordinal))
        {
            failures.Add(
                $"operation {operation} span set mismatch: expected [{string.Join(", ", sortedExpected)}], " +
                $"got [{string.Join(", ", actual)}]");
        }

        return new OperationSpanSet(operation, sortedExpected, actual);
    }

    private static void RequireExactTagKeys(CapturedActivity activity, string[] expected, ICollection<string> failures)
    {
        var actual = activity.Tags.Keys.Order(StringComparer.Ordinal).ToArray();
        var sortedExpected = expected.Order(StringComparer.Ordinal).ToArray();
        if (!actual.SequenceEqual(sortedExpected, StringComparer.Ordinal))
        {
            failures.Add(
                $"span {activity.Name} tag keys mismatch: expected [{string.Join(", ", sortedExpected)}], " +
                $"got [{string.Join(", ", actual)}]");
        }
    }

    private static void RequireTag(CapturedActivity activity, string key, string expected, ICollection<string> failures)
    {
        if (!activity.Tags.TryGetValue(key, out var actual))
        {
            failures.Add($"missing {key}");
            return;
        }

        if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(GraphQlReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealGraphQlJsonContext : JsonSerializerContext;
