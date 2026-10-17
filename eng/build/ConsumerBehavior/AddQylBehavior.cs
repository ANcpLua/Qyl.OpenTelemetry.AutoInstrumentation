using System.Globalization;
using System.Text.Json;

namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// AddQyl is registered once, never blocks on a socket, deduplicates a source the consumer names
/// again, and exports nothing from the build-time OpenAPI document host. The fixture runs the real
/// registration path against a local OTLP/HTTP listener that counts export requests, so nothing here
/// reaches into SDK internals.
/// </summary>
static class AddQylBehavior
{
    private const string Fixture = "AddQylBehavior";
    private const string AssemblyName = "Qyl.AddQylBehavior";

    // Four 100 ms probes plus DNS is ~400 ms. The fixture warms the registration path up with discovery
    // disabled first, so the timed call pays the probe and nothing else; this bound is far below the
    // probe's cost and far above the registration work that remains.
    private const double AddQylBudgetMilliseconds = 100;

    public static void Run()
    {
        var directory = FixtureWorkspace.Create("addqyl-behavior");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / $"{AssemblyName}.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" +
            $"    <RootNamespace>{AssemblyName}</RootNamespace>\n" +
            $"    <AssemblyName>{AssemblyName}</AssemblyName>\n",
            $"    <ProjectReference Include=\"{Repo.HostingProject}\" />\n");
        DotNet.Build(project, workingDirectory: directory);

        var completed = ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(directory, AssemblyName));
        Gate.Require(completed.ExitCode == 0, () => "AddQyl behaviour fixture failed\n" + completed.Describe());

        var start = completed.Stdout.IndexOf("{\n", StringComparison.Ordinal);
        Gate.Require(start >= 0, () => $"AddQyl behaviour fixture emitted no JSON report\nstdout={completed.Stdout}");
        JsonElement report;
        try
        {
            report = JsonDocument.Parse(completed.Stdout[start..]).RootElement;
        }
        catch (JsonException error)
        {
            throw Gate.Fail($"AddQyl behaviour fixture emitted invalid JSON: {error.Message}\nstdout={completed.Stdout}");
        }

        var elapsed = report.GetProperty("AddQylMilliseconds").GetDouble();
        Gate.Require(elapsed < AddQylBudgetMilliseconds,
            $"AddQyl blocked for {elapsed.ToString(CultureInfo.InvariantCulture)} ms with nothing listening; the collector probe must run off the caller's thread (budget {AddQylBudgetMilliseconds} ms)");

        var exports = report.GetProperty("ExportRequests").GetInt32();
        Gate.Require(exports == 1,
            $"one span produced {exports} OTLP export requests; two AddQyl calls must build one exporter and one of each processor");

        var buildTimeExports = report.GetProperty("BuildTimeExportRequests").GetInt32();
        Gate.Require(buildTimeExports == 0,
            $"the build-time document host sent {buildTimeExports} OTLP export requests; a host that only builds the OpenAPI document must reach no collector, whatever OTEL_EXPORTER_OTLP_ENDPOINT says");

        Log.Information("  - AddQyl returned in {Elapsed} ms with no collector listening", elapsed.ToString(CultureInfo.InvariantCulture));
        Log.Information("  - two AddQyl calls, one span, {Exports} OTLP export request", exports);
        Log.Information("  - GetDocument.Insider exported {Exports} times with the endpoint set", buildTimeExports);
        Log.Information("addqyl-behavior-ok");
    }
}
