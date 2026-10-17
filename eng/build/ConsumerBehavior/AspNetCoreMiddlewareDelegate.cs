namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// Hand-written ASP.NET Core middleware does not break the build. Convention-based middleware calls
/// the next hop as <c>next(context)</c>, a <c>RequestDelegate.Invoke</c> with MethodKind.DelegateInvoke,
/// which the C# interceptors feature cannot intercept (CS9207). The generator must skip it, and an
/// unrelated DbCommand call site in the same consumer proves it still emits supported interceptors —
/// selective, not silent.
/// </summary>
static class AspNetCoreMiddlewareDelegate
{
    private const string Fixture = "AspNetCoreMiddlewareDelegate";
    private const string RequestDelegateInterceptorToken = "AspNetCoreRequestDelegate_Invoke";
    private const string ControlInterceptorToken = "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedDbCommand.Execute(";

    public static void Run()
    {
        var directory = FixtureWorkspace.Create("middleware-delegate-consumer");
        FixtureWorkspace.CopyFixture(Fixture, directory, "Program.cs");
        var project = directory / "MiddlewareDelegateConsumer.csproj";
        ConsumerFixture.WriteProject(project, "Microsoft.NET.Sdk.Web",
            ConsumerFixture.EmitGeneratedProperties,
            $"    <ProjectReference Include=\"{Repo.CoreProject}\" />\n" +
            $"    <ProjectReference Include=\"{Repo.GeneratorProject}\"\n" +
            "                      OutputItemType=\"Analyzer\"\n" +
            "                      ReferenceOutputAssembly=\"false\" />\n" +
            ConsumerFixture.RemoveGeneratedItem,
            Repo.CoreBuildTargets);

        // A delegate-invoke interceptor would fail this build with CS9207; the clean build is the
        // primary assertion.
        DotNet.Build(project, workingDirectory: directory);

        var files = ConsumerFixture.GeneratedInterceptors(directory);
        Gate.Require(files.Length <= 1, $"expected at most one generated interceptor source file, found {files.Length}");
        var text = files.Length == 0 ? string.Empty : File.ReadAllText(files[0]);
        Gate.Require(!text.Contains(RequestDelegateInterceptorToken, StringComparison.Ordinal),
            "generator emitted an un-interceptable RequestDelegate.Invoke interceptor (CS9207 risk)");
        Gate.Require(text.Contains(ControlInterceptorToken, StringComparison.Ordinal),
            "generator emitted no control DbCommand interceptor — it went silent instead of selective");

        Log.Information("aspnetcore-middleware-delegate-ok");
    }
}
