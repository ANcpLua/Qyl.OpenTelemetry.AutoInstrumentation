namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// Referencing Qyl.Telemetry.AutoInstrumentation.Hosting changes nothing an application observes:
/// the same program, with and without the package, has the same exit code, stdout and stderr.
/// </summary>
static class ConsumerParity
{
    private const string Fixture = "ConsumerParity";

    public static void Run()
    {
        Gate.Require(Repo.AutoHostingProject.FileExists(), $"hosting project missing: {Repo.AutoHostingProject}");
        var version = LocalFeed.EnsurePacked();
        var baseline = BuildAndRun("consumer-parity-baseline", null);
        var instrumented = BuildAndRun("consumer-parity-instrumented", version);

        var mismatches = new List<string>();
        if (baseline.ExitCode != instrumented.ExitCode)
            mismatches.Add($"exit code: baseline={baseline.ExitCode} instrumented={instrumented.ExitCode}");
        if (baseline.Stdout != instrumented.Stdout)
            mismatches.Add($"stdout:\nBASELINE\n{baseline.Stdout}\nINSTRUMENTED\n{instrumented.Stdout}");
        if (baseline.Stderr != instrumented.Stderr)
            mismatches.Add($"stderr:\nBASELINE\n{baseline.Stderr}\nINSTRUMENTED\n{instrumented.Stderr}");
        Gate.Require(mismatches.Count == 0,
            () => "consumer behavior changed with Qyl.Telemetry.AutoInstrumentation.Hosting reference:\n" + string.Join("\n", mismatches));

        Log.Information("consumer-behavior-ok");
    }

    private static ProcessResult BuildAndRun(string name, string? instrumentedVersion)
    {
        var directory = FixtureWorkspace.Create(name);
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n",
            instrumentedVersion is null ? string.Empty : ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.Hosting", instrumentedVersion));
        DotNet.Build(project, workingDirectory: directory);
        return ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(directory, "Consumer"));
    }
}
