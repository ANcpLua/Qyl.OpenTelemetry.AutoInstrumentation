namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// A ProjectReference consumer gets the generated interceptor at all, managed and NativeAOT. The
/// ADO.NET DbCommand lane is what stays an interceptor after 15.0.0: System.Data.Common declares no
/// ActivitySource, so a generated call-site interceptor is the only producer there is. The probe
/// implements the abstract command itself, so the consumer needs no database and no provider package.
/// </summary>
static class ProjectReferenceBehavior
{
    private const string Fixture = "ProjectReferenceBehavior";

    public static void Run()
    {
        DotNet.Build(Repo.GeneratorProject);
        var directory = FixtureWorkspace.Create("projectreference-consumer");
        var project = directory / "Consumer.csproj";
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" + ConsumerFixture.EmitGeneratedProperties,
            $"    <ProjectReference Include=\"{Repo.CoreProject}\" />\n" +
            $"    <ProjectReference Include=\"{Repo.GeneratorProject}\"\n" +
            "                      Condition=\"'$(PublishAot)' != 'true'\"\n" +
            "                      OutputItemType=\"Analyzer\"\n" +
            "                      ReferenceOutputAssembly=\"false\"\n" +
            "                      GlobalPropertiesToRemove=\"PublishAot;PublishSingleFile;PublishTrimmed;RuntimeIdentifier;RuntimeIdentifiers;SelfContained\" />\n" +
            $"    <Analyzer Include=\"{Repo.GeneratorAssembly}\" Condition=\"'$(PublishAot)' == 'true'\" />\n" +
            ConsumerFixture.RemoveGeneratedItem,
            Repo.CoreBuildTargets);
        var expected = FixtureWorkspace.ReadFixture(Fixture, "expected.txt");

        DotNet.Build(project, workingDirectory: directory);
        var interceptor = ConsumerFixture.SingleGeneratedInterceptor(directory, "ProjectReference consumer");
        ConsumerFixture.RequireTokens("generated interceptor source", interceptor,
        [
            "#nullable enable",
            "Qyl.Telemetry.AutoInstrumentation.Generated",
            "file sealed class InterceptsLocationAttribute",
            "global::ProbeCommand receiver",
            "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedDbCommand.Execute(",
        ]);

        var managed = ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(directory, "Consumer"));
        Gate.Require(managed.ExitCode == 0, () => "managed ProjectReference consumer failed: " + managed.Describe());
        ConsumerFixture.RequireOutput("managed ProjectReference consumer output", managed.Stdout, expected);

        var executable = DotNet.PublishNativeAot(project, directory / "publish", ["-p:InvariantGlobalization=true"], workingDirectory: directory);
        var native = ConsumerFixture.RunExecutable(executable);
        Gate.Require(native.ExitCode == 0, () => "NativeAOT ProjectReference consumer failed: " + native.Describe());
        ConsumerFixture.RequireOutput("NativeAOT ProjectReference consumer output", native.Stdout, expected);

        Log.Information("projectreference-behavior-ok");
    }
}
