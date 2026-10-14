namespace Qyl.Build;

/// <summary>
/// The application scope, through the packages alone: a consumer's <c>Spans.Work</c> span is exported
/// by <c>AddQyl</c> under <c>Qyl.Telemetry.Application</c>, versioned with the registry release and
/// carrying the qyl schema URL, and a consumer that builds its own <c>ActivitySource</c> fails its
/// build with QYL0111. Both consumers reference <c>Qyl.Telemetry.Hosting</c> and nothing else, so the
/// failure also proves the semantic-convention analyzers flow through it.
/// </summary>
static class ApplicationScopeBehavior
{
    private const string WorkFixture = "ApplicationScopeBehavior";
    private const string RawSourceFixture = "RawActivitySource";

    public static void Run()
    {
        var version = LocalFeed.EnsurePacked();
        var registry = Repo.Pin(Repo.SemanticConventionsPackage);

        var work = Consumer("application-scope-work", WorkFixture, version);
        DotNet.Build(work, workingDirectory: work.Parent);
        var completed = ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(work.Parent, "Consumer"));
        Gate.Require(completed.ExitCode == 0, () => "application-scope consumer failed\n" + completed.Describe());
        ConsumerFixture.RequireOutput("application-scope span", completed.Stdout,
            "exported=1\n" +
            "span=checkout|Internal\n" +
            $"scope=Qyl.Telemetry.Application|{registry}|https://qyl.at/schemas/{registry}\n" +
            "qyl.instrumentation.domain=app\n" +
            "qyl.app.order.id=o-1\n");
        Log.Information("  - Spans.Work exported under Qyl.Telemetry.Application {Version} with the qyl schema URL", registry);

        var raw = Consumer("application-scope-raw-source", RawSourceFixture, version);
        var build = Shell.Run("dotnet", ["build", raw, "-c", "Release", "-maxcpucount:4", "-nodeReuse:false", "-v", "quiet", "-nologo"], raw.Parent);
        var output = build.Stdout + build.Stderr;
        Gate.Require(build.ExitCode != 0, () => "a consumer's own new ActivitySource built; QYL0111 did not reach it through Qyl.Telemetry.Hosting\n" + build.Describe());
        Gate.Require(output.Contains("error QYL0111", StringComparison.Ordinal) && output.Contains("Program.cs(10,", StringComparison.Ordinal),
            () => "the raw-source consumer failed, but not with QYL0111 on its new ActivitySource\n" + build.Describe());
        Log.Information("  - a consumer's new ActivitySource fails its build with QYL0111");
        Log.Information("application-scope-behavior-ok");
    }

    private static AbsolutePath Consumer(string name, string fixture, string version)
    {
        var directory = FixtureWorkspace.Create(name);
        FixtureWorkspace.CopyFixture(fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n",
            ConsumerFixture.PackageReference("Qyl.Telemetry.Hosting", version) +
            ConsumerFixture.PackageReference("OpenTelemetry.Exporter.InMemory", Repo.Pin("OpenTelemetry.Exporter.InMemory")));
        return project;
    }
}
