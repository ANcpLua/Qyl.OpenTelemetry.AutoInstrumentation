namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// The qyl runtime works inside a NativeAOT-published package consumer: System.Net.Http emits the
/// client span of a request to a closed loopback port, and the processor AddQyl registers stamps the
/// qyl domain onto it. The exact span shape — name, kind and every tag — is pinned.
/// </summary>
static class NativeAotConsumer
{
    private const string Fixture = "NativeAotConsumer";

    public static void Run()
    {
        var version = LocalFeed.EnsurePacked();
        var directory = FixtureWorkspace.Create("nativeaot-consumer");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n",
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.Hosting", version) +
            ConsumerFixture.PackageReference("Qyl.Telemetry.Hosting", version) +
            ConsumerFixture.PackageReference("OpenTelemetry.Exporter.InMemory", Repo.Pin("OpenTelemetry.Exporter.InMemory")));

        var executable = DotNet.PublishNativeAot(project, directory / "publish", workingDirectory: directory);
        var completed = ConsumerFixture.RunExecutable(executable);
        Gate.Require(completed.ExitCode == 0, () => "NativeAOT consumer failed\n" + completed.Describe());
        Gate.Require(completed.Stderr.Length == 0, () => $"NativeAOT consumer wrote stderr:\n{completed.Stderr}");
        ConsumerFixture.RequireOutput("NativeAOT verified", completed.Stdout, FixtureWorkspace.ReadFixture(Fixture, "expected.txt"));

        Log.Information("nativeaot-consumer-ok");
    }
}
