using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Qyl.Telemetry.AutoInstrumentation.GeneratedCode;
using Qyl.Telemetry.AutoInstrumentation.SourceGenerators;

namespace Qyl.Telemetry.Integration.Tests;

[TestClass]
public sealed class GeneratorContractTests(TestContext testContext)
{
    private static readonly CSharpParseOptions ParseOptions = new CSharpParseOptions(LanguageVersion.Preview)
        .WithFeatures([new KeyValuePair<string, string>("InterceptorsNamespaces", "Qyl.Telemetry.AutoInstrumentation.Generated")]);

    [TestMethod]
    public void Changed_dependency_signature_is_diagnosed_without_emitting_an_interceptor()
    {
        var result = Run("""
// A call site that names a declared integration receiver and method but does not fit the declared
// shape. QylUnmatchedCommand derives from System.Data.Common.DbCommand, so the receiver matches, and
// the method name ExecuteScalar matches the DbCommand intercept declaration -- but the signature
// takes an int and returns string, which the DbCommand shape does not describe.
//
// The generator must emit NO interceptor for it and report exactly one QYL1001 instead. This is the
// failure mode a library major introduces when it changes an intercepted signature: silence here
// would mean instrumentation disappearing with no signal, and emitting an interceptor anyway would
// break the consumer's build.
internal static class ShapeMismatchProbe
{
    internal static string Unmatched(QylUnmatchedCommand command)
        => command.ExecuteScalar(1);
}

internal sealed class QylUnmatchedCommand : SnapshotCommand
{
    internal string ExecuteScalar(int id)
        => id.ToString(System.Globalization.CultureInfo.InvariantCulture);
}
""", commandSource: true);
        var diagnostic = Assert.ContainsSingle(result.Diagnostics);
        Assert.AreEqual("QYL1001", diagnostic.Id);
        Assert.AreEqual(DiagnosticSeverity.Info, diagnostic.Severity);
        Assert.Contains("QylUnmatchedCommand.ExecuteScalar", diagnostic.GetMessage());
        Assert.Contains("not instrumented", diagnostic.GetMessage());
        Assert.IsFalse(result.GeneratedSources.Any(static source => source.HintName == "QylAutoInstrumentation.Interceptors.g.cs"));
    }

    [TestMethod]
    public void GenAi_libraries_without_their_opt_ins_report_each_missing_activation()
    {
        var result = Run("""
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.AI;

// QYL1002's three GenAI rows: each library is used here and never opted in, so the generator reports
// one warning per row. Nothing calls these methods; they only have to compile.
internal static class GenAiOptInsProbe
{
    internal static object Chat(IChatClient client) => client.GetResponseAsync("probe");

    internal static object Agent(AIAgent agent) => agent.RunAsync("probe");

    internal static object Workflow(WorkflowBuilder builder) => builder.Build();
}
""");
        Assert.HasCount(3, result.Diagnostics);
        foreach (var library in new[] { "Microsoft.Extensions.AI", "Microsoft.Agents.AI", "Microsoft.Agents.AI.Workflows" })
        {
            var diagnostic = Assert.ContainsSingle(result.Diagnostics.Where(d => d.GetMessage().StartsWith("qyl subscribes " + library + "'s ActivitySource", StringComparison.Ordinal)));
            Assert.AreEqual("QYL1002", diagnostic.Id);
            Assert.AreEqual(DiagnosticSeverity.Warning, diagnostic.Severity);
        }
    }

    [TestMethod]
    public void Valid_db_command_emits_compilable_interceptor_for_the_current_runtime_abi()
    {
        var result = Run("""
internal static class Consumer
{
    internal static object? Run(SnapshotCommand command) => command.ExecuteScalar();
}
""", commandSource: true);
        Assert.IsEmpty(result.Diagnostics);
        var generated = Assert.ContainsSingle(result.GeneratedSources.Where(static source => source.HintName == "QylAutoInstrumentation.Interceptors.g.cs")).SourceText.ToString();
        Assert.Contains(nameof(QylGeneratedCodeAbi) + "." + nameof(QylGeneratedCodeAbi.V28), generated);
        Assert.Contains("QylInterceptedDbCommand.Execute(", generated);
    }

    private GeneratorRunResult Run(string source, bool commandSource = false)
    {
        var trees = new List<SyntaxTree> { CSharpSyntaxTree.ParseText(source, ParseOptions, "Consumer.cs", cancellationToken: testContext.CancellationToken) };
        if (commandSource)
            trees.Add(CSharpSyntaxTree.ParseText("using System; using System.Threading; using System.Threading.Tasks;\n" + File.ReadAllText(Repo.Root / "tests/Qyl.Telemetry.AutoInstrumentation.SourceGenerators.Snapshots/Fixture/SnapshotCommand.cs"), ParseOptions, "SnapshotCommand.cs", cancellationToken: testContext.CancellationToken));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!).Split(Path.PathSeparator)
            .Append(typeof(QylGeneratedCodeAbi).Assembly.Location).Distinct(StringComparer.Ordinal)
            .Select(static path => MetadataReference.CreateFromFile(path));
        var compilation = CSharpCompilation.Create("GeneratorConsumer", trees, references,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, nullableContextOptions: NullableContextOptions.Enable));
        Assert.IsEmpty(compilation.GetDiagnostics(testContext.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray());
        GeneratorDriver driver = CSharpGeneratorDriver.Create([new QylAutoInstrumentationGenerator().AsSourceGenerator()], parseOptions: ParseOptions);
        driver = driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out _, testContext.CancellationToken);
        Assert.IsEmpty(updated.GetDiagnostics(testContext.CancellationToken).Where(static diagnostic => diagnostic.Severity == DiagnosticSeverity.Error).ToArray());
        return Assert.ContainsSingle(driver.GetRunResult().Results);
    }
}
