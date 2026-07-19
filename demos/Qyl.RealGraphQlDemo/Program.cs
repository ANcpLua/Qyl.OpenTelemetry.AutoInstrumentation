using System.Diagnostics;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using GraphQL;
using GraphQL.Resolvers;
using GraphQL.Types;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

IDocumentExecuter executer = new DocumentExecuter();
var schema = new Schema(GraphQlServices.Instance) { Query = new ProbeQuery() };
var result = await executer.ExecuteAsync(new ExecutionOptions
{
    Schema = schema,
    Query = GraphQlReport.QueryText,
    OperationName = GraphQlReport.OperationName,
});

if (result.Errors is { Count: > 0 })
    Console.WriteLine("graphql-errors=" + result.Errors.Count.ToString(CultureInfo.InvariantCulture));
else
    Console.WriteLine("graphql-success=true");

try
{
    await executer.ExecuteAsync(null!);
}
catch (ArgumentNullException exception)
{
    Console.WriteLine("expected-graphql-error=" + exception.GetType().Name);
}

var report = GraphQlReport.Create(
    RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
    captured.ToArray());

var json = JsonSerializer.Serialize(report, RealGraphQlJsonContext.Default.GraphQlReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

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
    {
        if (serviceType == typeof(ProbeQuery))
            return new ProbeQuery();

        return null;
    }
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
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
    CapturedActivity[] Activities)
{
    public const string OperationName = "QylHello";
    public const string QueryText = "query QylHello { hello }";

    public static GraphQlReport Create(string runtimeMode, CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var graphQlSpans = activities
            .Where(static activity =>
                activity.Tags.TryGetValue("qyl.instrumentation.domain", out var domain) &&
                StringComparer.Ordinal.Equals(domain, "graphql"))
            .ToArray();

        if (graphQlSpans.Length != 2)
            failures.Add($"expected 2 GraphQL execute spans, got {graphQlSpans.Length}");

        var success = graphQlSpans.FirstOrDefault(static span => StringComparer.Ordinal.Equals(span.Status, "Unset"));
        var error = graphQlSpans.FirstOrDefault(static span => StringComparer.Ordinal.Equals(span.Status, "Error"));

        if (success is null)
        {
            failures.Add("missing successful GraphQL execute span");
        }
        else
        {
            ExpectTag(success, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes.OperationName, OperationName, failures);
            ExpectTag(success, Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.Graphql.GraphqlAttributes.Document, QueryText, failures);
        }

        if (error is null)
            failures.Add("missing error GraphQL execute span");
        else
            ExpectTag(error, Qyl.OpenTelemetry.SemanticConventions.Attributes.Error.ErrorAttributes.Type, typeof(ArgumentNullException).FullName!, failures);

        foreach (var span in graphQlSpans)
        {
            if (!StringComparer.Ordinal.Equals(span.Name, "GraphQL execute"))
                failures.Add($"unexpected GraphQL span name: {span.Name}");

            if (!StringComparer.Ordinal.Equals(span.Kind, "Internal"))
                failures.Add($"expected kind Internal, got {span.Kind}");
        }

        return new GraphQlReport(runtimeMode, failures.Count is 0, failures.ToArray(), graphQlSpans);
    }

    private static void ExpectTag(
        CapturedActivity activity,
        string key,
        string expected,
        List<string> failures)
    {
        if (!activity.Tags.TryGetValue(key, out var actual))
            failures.Add($"missing {key}");
        else if (!StringComparer.Ordinal.Equals(actual, expected))
            failures.Add($"expected {key}={expected}, got {actual}");
    }
}

[JsonSerializable(typeof(GraphQlReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class RealGraphQlJsonContext : JsonSerializerContext;
