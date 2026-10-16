namespace Qyl.Build;

/// <summary>
/// The application scope, through the packages alone: a consumer's <c>Spans.Work</c> span is exported
/// by <c>AddQyl</c> under <c>Qyl.Telemetry.Application</c>, versioned with the registry release and
/// carrying the qyl schema URL, on a resource whose <c>service.version</c> is the consumer build's own
/// version and source revision (the generated <c>QylConsumerBuild</c>) and whose
/// <c>deployment.environment.name</c> is the host environment; and a consumer may register its own
/// <c>ActivitySource</c> and export native typed application attributes. Both consumers reference
/// <c>Qyl.Telemetry.Hosting</c> and the real in-memory SDK exporter.
/// </summary>
static class ApplicationScopeBehavior
{
    private const string WorkFixture = "ApplicationScopeBehavior";
    private const string RawSourceFixture = "RawActivitySource";

    // The consumer build's own version and revision; the SDK joins them as SemVer build metadata.
    private const string ConsumerVersion = "3.1.4";
    private const string ConsumerRevision = "4f1c0de";

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
            "checkout.order.id=o-1\n" +
            "service.name=qyl-application-scope-behavior\n" +
            "service.namespace=shop\n" +
            $"service.version={ConsumerVersion}+{ConsumerRevision}\n" +
            "deployment.environment.name=Production\n");
        Log.Information("  - Spans.Work exported under Qyl.Telemetry.Application {Version} with the qyl schema URL", registry);
        Log.Information("  - service.version {Version}+{Revision} from the consumer build, deployment.environment.name from the host", ConsumerVersion, ConsumerRevision);

        var raw = Consumer("application-scope-raw-source", RawSourceFixture, version);
        DotNet.Build(raw, workingDirectory: raw.Parent);
        var rawCompleted = ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(raw.Parent, "Consumer"));
        Gate.Require(rawCompleted.ExitCode == 0, () => "native-source consumer failed\n" + rawCompleted.Describe());
        ConsumerFixture.RequireOutput("native application attributes", rawCompleted.Stdout,
            "exported=1\n" +
            "scope=Shop.Orders\n" +
            "checkout.sequence=Int64|9007199254740993\n" +
            "checkout.accepted=Boolean|False\n" +
            "checkout.labels=String[]|first,second\n");
        Log.Information("  - an application-owned ActivitySource exports native typed attributes through AddQyl");
        Log.Information("application-scope-behavior-ok");
    }

    private static AbsolutePath Consumer(string name, string fixture, string version)
    {
        var directory = FixtureWorkspace.Create(name);
        FixtureWorkspace.CopyFixture(fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" +
            $"    <Version>{ConsumerVersion}</Version>\n" +
            $"    <SourceRevisionId>{ConsumerRevision}</SourceRevisionId>\n",
            ConsumerFixture.PackageReference("Qyl.Telemetry.Hosting", version) +
            ConsumerFixture.PackageReference("OpenTelemetry.Exporter.InMemory", Repo.Pin("OpenTelemetry.Exporter.InMemory")));
        return project;
    }
}
