namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// Negative space: a disabled instrumentation emits zero qyl telemetry. The ADO.NET DbCommand
/// interceptor is the lane the toggles gate after 15.0.0 — System.Data.Common declares no
/// ActivitySource, so the qyl span is the only span and its absence is the kill switch working. The
/// logs signal declares no instrumentation; OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED is proved by
/// the ilogger demo lane against the log-record exporter Qyl.Telemetry.Hosting registers.
/// </summary>
static class InstrumentationDisabledBehavior
{
    private const string Fixture = "InstrumentationDisabledBehavior";

    public static void Run()
    {
        var version = LocalFeed.EnsurePacked();
        var directory = FixtureWorkspace.Create("instrumentation-disabled-consumer");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" + ConsumerFixture.EmitGeneratedProperties,
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation", version) +
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.Hosting", version) +
            ConsumerFixture.RemoveGeneratedItem);
        DotNet.Build(project, workingDirectory: directory);
        var assembly = ConsumerFixture.Assembly(directory, "Consumer");

        // Control: the interceptor is present and emits exactly one span.
        Expect("enabled (control)", assembly, [], 1);
        // Per-integration trace kill switch.
        Expect("adonet trace instrumentation disabled", assembly, new() { ["OTEL_DOTNET_AUTO_TRACES_ADONET_INSTRUMENTATION_ENABLED"] = "false" }, 0);
        // Signal-level kill switch.
        Expect("traces signal disabled", assembly, new() { ["OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED"] = "false" }, 0);
        // Global kill switch.
        Expect("global instrumentation disabled", assembly, new() { ["OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED"] = "false" }, 0);
        // Selectivity: an unrelated integration's switch leaves the ADO.NET span alone.
        Expect("unrelated integration disabled", assembly, new() { ["OTEL_DOTNET_AUTO_TRACES_SQLCLIENT_INSTRUMENTATION_ENABLED"] = "false" }, 1);

        Log.Information("instrumentation-disabled-behavior-ok");
    }

    private static void Expect(string name, AbsolutePath assembly, Dictionary<string, string> overrides, int spans)
    {
        var result = ConsumerFixture.RunAssembly(assembly, overrides);
        Gate.Require(result.ExitCode == 0, () => $"{name}: consumer run failed\n" + result.Describe());
        Gate.Require(result.Stderr.Length == 0, () => $"{name}: consumer wrote stderr:\n{result.Stderr}");
        var actual = result.Stdout.Trim();
        var expected = $"adonet.spans={spans}";
        Gate.Require(actual == expected, $"{name}: expected '{expected}', got '{actual}'");
        Log.Information("  {Scenario}: {Actual}", name, actual);
    }
}
