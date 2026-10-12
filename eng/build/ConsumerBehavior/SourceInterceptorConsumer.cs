using System.Text.RegularExpressions;

namespace Qyl.Build;

/// <summary>
/// A PackageReference consumer of Qyl.Telemetry.AutoInstrumentation alone gets the generated
/// DbCommand interceptor, managed and NativeAOT, and it records the failing call as well as the
/// successful ones against a real SQLite provider.
/// </summary>
static partial class SourceInterceptorConsumer
{
    private const string Fixture = "SourceInterceptorConsumer";

    public static void Run()
    {
        var version = LocalFeed.EnsurePacked();
        var directory = FixtureWorkspace.Create("source-interceptor-consumer");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "Consumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" + ConsumerFixture.EmitGeneratedProperties,
            ConsumerFixture.PackageReference("Qyl.Telemetry.AutoInstrumentation", version) +
            ConsumerFixture.PackageReference("Microsoft.Data.Sqlite", Repo.Pin("Microsoft.Data.Sqlite")) +
            ConsumerFixture.PackageReference("SQLitePCLRaw.lib.e_sqlite3", Repo.Pin("SQLitePCLRaw.lib.e_sqlite3")) +
            ConsumerFixture.RemoveGeneratedItem);
        var expected = FixtureWorkspace.ReadFixture(Fixture, "expected.txt");

        DotNet.Build(project, workingDirectory: directory);
        VerifyGeneratedSource(directory);
        Verify("managed consumer", ConsumerFixture.RunAssembly(ConsumerFixture.Assembly(directory, "Consumer")), expected);

        var executable = DotNet.PublishNativeAot(project, directory / "publish", workingDirectory: directory);
        Verify("NativeAOT consumer", ConsumerFixture.RunExecutable(executable), expected);

        Log.Information("source-interceptor-consumer-ok");
    }

    private static void VerifyGeneratedSource(AbsolutePath directory)
    {
        var files = ConsumerFixture.GeneratedInterceptors(directory);
        Gate.Require(files.Length == 1, $"expected one generated interceptor source, found {files.Length}");
        var text = File.ReadAllText(files[0]);
        foreach (var token in new[]
        {
            "namespace Qyl.Telemetry.AutoInstrumentation.Generated",
            "DbCommand_ExecuteScalar_",
            "QylInterceptedDbCommand.Execute(",
            "\"contractKeys\":[\"signals.traces.ADONET\"]",
        })
        {
            if (!text.Contains(token, StringComparison.Ordinal))
            {
                var names = EmittedNames().Matches(text).Select(static match => match.Value).Distinct().Order(StringComparer.Ordinal);
                throw Gate.Fail($"generated interceptor source missing token: {token}; it emitted: [{string.Join(", ", names)}]");
            }
        }
    }

    private static void Verify(string name, ProcessResult completed, string expected)
    {
        if (completed.ExitCode != 0 || completed.Stderr.Length != 0 || completed.Stdout != expected)
            throw Gate.Fail($"{name} failed\nexit={completed.ExitCode}\nEXPECTED\n{expected}\nACTUAL\n{completed.Stdout}\nSTDERR\n{completed.Stderr}");
    }

    [GeneratedRegex("[A-Za-z]+_[A-Za-z]+_[0-9]+")]
    private static partial Regex EmittedNames();
}
