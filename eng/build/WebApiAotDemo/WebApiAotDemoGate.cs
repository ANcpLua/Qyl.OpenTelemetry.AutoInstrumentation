using System.Text.Json;

namespace Qyl.Build.WebApiAotDemo;

/// <summary>
/// The NativeAOT web API consumer: ASP.NET Core, HttpClient, EF Core with a compiled model and
/// SqlClient in one package-consumer application, published NativeAOT under the external
/// WebApiAotDemo vendor-warning policy (a publish that became warning-clean fails, so the boundary gets
/// removed), run, and its report compared byte for byte with the committed verified file.
/// </summary>
static class WebApiAotDemoGate
{
    private const string Fixture = "WebApiAotDemo";
    private const string Policy = "WebApiAotDemo";

    private static readonly string[] EfCoreCompiledModelSources =
    [
        "ProbeContext.cs",
        "CompiledModels/ProbeContextAssemblyAttributes.cs",
        "CompiledModels/ProbeContextModel.cs",
        "CompiledModels/ProbeContextModelBuilder.cs",
        "CompiledModels/ProbeItemEntityType.cs",
        "CompiledModels/ProbeItemUnsafeAccessors.cs",
    ];

    private static AbsolutePath VerifiedPath => Repo.Fixtures / Fixture / "verified" / "report.json";

    public static void Run(bool updateVerified)
    {
        var version = LocalFeed.EnsurePacked();
        var directory = FixtureWorkspace.Create("webapi-aot-demo");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "WebApiAotDemo.csproj";
        var compiledModel = string.Concat(EfCoreCompiledModelSources.Select(static source =>
        {
            var path = Repo.DemosDirectory / "Qyl.RealEfCoreDemo" / source;
            Gate.Require(path.FileExists(), $"missing EF Core compiled-model source {Repo.Relative(path)}");
            return $"    <Compile Include=\"{path}\" Link=\"EfCoreCompiledModel/{path.Name}\" />\n";
        }));
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk.Web",
            "    <OutputType>Exe</OutputType>\n" +
            "    <DefineConstants>$(DefineConstants);USE_COMPILED_MODEL</DefineConstants>\n",
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.Hosting", version) +
            ConsumerFixture.PackageReference("Qyl.Telemetry.Hosting", version) +
            ConsumerFixture.PackageReference("OpenTelemetry.Exporter.InMemory", Repo.Pin("OpenTelemetry.Exporter.InMemory")) +
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore", version) +
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation.SqlClient", version) +
            ConsumerFixture.PackageReference("Microsoft.EntityFrameworkCore.Sqlite", Repo.Pin("Microsoft.EntityFrameworkCore.Sqlite")) +
            ConsumerFixture.PackageReference("Microsoft.Data.SqlClient", Repo.Pin("Microsoft.Data.SqlClient")) +
            ConsumerFixture.PackageReference("SQLitePCLRaw.lib.e_sqlite3", Repo.Pin("SQLitePCLRaw.lib.e_sqlite3")) +
            compiledModel);

        var output = directory / "publish";
        var outcome = AotPublish.Publish(project, output, strict: false);
        Gate.Require(outcome.ExitCode == 0, () => $"external AOT publish failed: exit={outcome.ExitCode}; " + (outcome.Diagnostics.FirstOrDefault() ?? outcome.Tail));
        // Strict promotion: a vendor boundary that no longer produces a warning must be removed.
        Gate.Require(outcome.Diagnostics.Count > 0, $"{Policy} is now warning-clean; remove its vendor boundary");
        var (valid, detail) = AotPublish.ValidateWarningPolicy(Policy, project, outcome.Diagnostics);
        Gate.Require(valid, $"{Policy} warning policy failed: {detail}");
        Log.Information("external-aot-publish-gate-ok {Policy}: {Detail}", Policy, detail);

        var executable = output / Repo.ExecutableName("WebApiAotDemo");
        Gate.Require(executable.FileExists(), $"NativeAOT web API executable missing: {executable}");
        var completed = ConsumerFixture.RunExecutable(executable);
        Gate.Require(completed.ExitCode == 0, () => "NativeAOT web API demo failed\n" + completed.Describe());
        Gate.Require(completed.Stderr.Length == 0, () => $"NativeAOT web API demo wrote stderr:\n{completed.Stderr}");

        string canonical;
        try
        {
            canonical = CanonicalJson.Render(completed.Stdout);
        }
        catch (JsonException error)
        {
            throw Gate.Fail($"NativeAOT web API demo did not print one JSON report: {error.Message}\nstdout={completed.Stdout}");
        }

        if (updateVerified)
        {
            VerifiedPath.Parent.CreateDirectory();
            File.WriteAllText(VerifiedPath, canonical);
            Log.Information("updated {Verified}", Repo.Relative(VerifiedPath));
            return;
        }

        Gate.Require(VerifiedPath.FileExists(), $"missing web API AOT verified: {VerifiedPath}");
        var expected = File.ReadAllText(VerifiedPath);
        if (canonical != expected)
        {
            var received = directory.Parent / "webapi-aot-demo.report.received.json";
            File.WriteAllText(received, canonical);
            throw Gate.Fail($"web API AOT verified mismatch\nexpected={VerifiedPath}\nreceived={received}\n{ConsumerFixture.Diff(expected, canonical)}");
        }

        Log.Information("webapi-aot-demo-ok");
    }
}
