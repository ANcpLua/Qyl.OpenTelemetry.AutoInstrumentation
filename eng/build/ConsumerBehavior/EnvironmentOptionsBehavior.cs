namespace Qyl.Build.ConsumerBehavior;

/// <summary>
/// Environment controls resolve to instrumentation decisions and capture-key maps in isolated
/// processes. The configuration probe reads internal state; the public HttpClient probe changes an emitted
/// span: the BCL's own System.Net.Http client span, whose url.full query qyl un-redacts by flipping the
/// runtime's System.Net.Http.DisableUriRedaction switch from the upstream opt-out variable.
/// </summary>
static class EnvironmentOptionsBehavior
{
    private const string Fixture = "EnvironmentOptionsBehavior";

    // The options probe is named for the core package's InternalsVisibleTo; the runtime probe is not,
    // so it proves the behavior through public API only.
    private const string OptionsProbe = "Qyl.Telemetry.AutoInstrumentation.VerifierProbe";
    private const string RuntimeProbe = "Qyl.OpenTelemetry.RuntimeProbe";

    public static void Run()
    {
        var version = LocalFeed.EnsurePacked();

        var options = FixtureWorkspace.Create("environment-options-consumer");
        FixtureWorkspace.CopyFixture(Fixture, options, "Program.cs");
        WriteProject(options, OptionsProbe, "Qyl.Telemetry.AutoInstrumentation", version);
        DotNet.Build(options / "Consumer.csproj", workingDirectory: options);
        var optionsAssembly = ConsumerFixture.Assembly(options, OptionsProbe);

        Scenario("default", optionsAssembly, [], "default.txt");
        Scenario("global disabled with HTTP trace override", optionsAssembly, new()
        {
            ["OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED"] = "false",
            ["OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED"] = "true",
        }, "global-disabled-http-trace-enabled.txt");
        Scenario("signal and signal-specific overrides", optionsAssembly, new()
        {
            ["OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED"] = "false",
            ["OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED"] = "true",
            ["OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED"] = "true",
            ["OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED"] = "true",
            ["OTEL_DOTNET_AUTO_TRACES_SQLCLIENT_INSTRUMENTATION_ENABLED"] = "false",
            ["OTEL_DOTNET_AUTO_METRICS_SQLCLIENT_INSTRUMENTATION_ENABLED"] = "false",
        }, "signal-and-specific-overrides.txt");
        Scenario("logs signal disabled", optionsAssembly, new()
        {
            ["OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED"] = "false",
        }, "logs-disabled.txt");
        Scenario("instrumentation options", optionsAssembly, new()
        {
            ["OTEL_DOTNET_AUTO_ENTITYFRAMEWORKCORE_SET_DBSTATEMENT_FOR_TEXT"] = "true",
            ["OTEL_DOTNET_AUTO_SQLCLIENT_SET_DBSTATEMENT_FOR_TEXT"] = "true",
            ["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"] = "X-Core-Request,Tenant",
            ["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"] = "X-Core-Response,ETag",
            ["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"] = "true",
            ["OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION"] = "true",
        }, "instrumentation-options.txt");
        Scenario("additional metric sources", optionsAssembly, new()
        {
            ["OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES"] = "YourCompany.CustomMeter, System.Net.Http, custom.case.Meter, YourCompany.CustomMeter",
        }, "additional-metric-sources.txt");

        var runtime = FixtureWorkspace.Create("environment-options-runtime-consumer");
        File.Copy(Repo.Fixtures / Fixture / "Runtime" / "Program.cs", runtime / "Program.cs");
        WriteProject(runtime, RuntimeProbe, "Qyl.Telemetry.AutoInstrumentation.Hosting", version);
        DotNet.Build(runtime / "Consumer.csproj", workingDirectory: runtime);
        var runtimeAssembly = ConsumerFixture.Assembly(runtime, RuntimeProbe);

        Scenario("runtime: the BCL redacts the query of its own url.full", runtimeAssembly, [], "runtime-default.txt");
        Scenario("runtime: url query redaction disabled", runtimeAssembly, new()
        {
            ["OTEL_DOTNET_EXPERIMENTAL_HTTPCLIENT_DISABLE_URL_QUERY_REDACTION"] = "true",
        }, "runtime-unredacted.txt");

        Log.Information("environment-options-behavior-ok");
    }

    private static void WriteProject(AbsolutePath directory, string assemblyName, string packageId, string version) =>
        ConsumerFixture.WriteProject(directory / "Consumer.csproj", "Microsoft.NET.Sdk",
            "    <OutputType>Exe</OutputType>\n" +
            $"    <AssemblyName>{assemblyName}</AssemblyName>\n",
            ConsumerFixture.PackageReference(packageId, version));

    private static void Scenario(string name, AbsolutePath assembly, Dictionary<string, string> overrides, string expectedFile)
    {
        var result = ConsumerFixture.RunAssembly(assembly, overrides);
        Gate.Require(result.ExitCode == 0, () => $"{name}: scenario failed\n" + result.Describe());
        Gate.Require(result.Stderr.Length == 0, () => $"{name}: scenario wrote stderr:\n{result.Stderr}");
        ConsumerFixture.RequireOutput(name, result.Stdout, FixtureWorkspace.ReadFixture(Fixture, "expected/" + expectedFile));
        Log.Information("  - {Scenario}: ok", name);
    }
}
