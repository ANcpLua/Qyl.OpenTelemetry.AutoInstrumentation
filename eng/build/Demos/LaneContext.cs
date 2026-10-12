namespace Qyl.Build;

/// <summary>
/// What a lane works with: the demo's environment (clean, plus QYL_ENDPOINT naming where the demo
/// exports, plus the testbed connection variables its services need), its builds and runs, and the
/// telemetry check for what the runs exported.
/// </summary>
sealed class LaneContext
{
    private static readonly TimeSpan s_runTimeout = TimeSpan.FromMinutes(10);

    public LaneContext(DemoLane lane, Uri endpoint, ITelemetryCheck telemetry, Testbed? testbed)
    {
        Lane = lane;
        Telemetry = telemetry;
        Endpoint = endpoint;
        var environment = Shell.CleanEnvironment();
        environment["QYL_ENDPOINT"] = endpoint.ToString().TrimEnd('/');
        foreach (var service in lane.Services)
        {
            Gate.Require(testbed is not null, $"lane {lane.Name} needs the testbed ({string.Join(", ", lane.Services)})");
            foreach (var (key, value) in testbed!.DemoEnvironment(service))
                environment[key] = value;
        }

        Environment = environment;
    }

    public DemoLane Lane { get; }

    public ITelemetryCheck Telemetry { get; }

    /// <summary>Where the demo exports: the collector's OTLP/HTTP base URL, or weaver's gRPC listener.</summary>
    public Uri Endpoint { get; }

    public IReadOnlyDictionary<string, string> Environment { get; }

    public IReadOnlyDictionary<string, string> With(params (string Key, string Value)[] overrides)
    {
        var environment = new Dictionary<string, string>(Environment, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
            environment[key] = value;
        return environment;
    }

    /// <summary>A Release build of the demo; returns its assembly.</summary>
    public string BuildManaged(params string[] extra)
    {
        DotNet.Build(Lane.Project, extra);
        var assembly = Repo.BinAssembly(Lane.Project);
        Gate.Require(File.Exists(assembly), $"managed assembly missing: {assembly}");
        return assembly;
    }

    /// <summary>The demo's NativeAOT binary, published by its AOT classification (once per build run).</summary>
    public string PublishNativeAot() => AotPublish.PublishDemo(Lane.Demo);

    public DemoRun RunManaged(string label, IReadOnlyDictionary<string, string>? environment = null, IReadOnlyList<string>? arguments = null) =>
        Execute(label, LaneRuntime.Managed, "dotnet", [Repo.BinAssembly(Lane.Project), .. arguments ?? []], Lane.Project.Parent, environment);

    public DemoRun RunNativeAot(string executable, string label, IReadOnlyDictionary<string, string>? environment = null, IReadOnlyList<string>? arguments = null) =>
        Execute(label, LaneRuntime.NativeAot, executable, arguments ?? [], Path.GetDirectoryName(executable)!, environment);

    /// <summary>A managed demo process that keeps running, for a lane that drives two processes.</summary>
    public RunningProcess StartManaged(IReadOnlyList<string> arguments, IReadOnlyDictionary<string, string>? environment = null) =>
        RunningProcess.Start("dotnet", [Repo.BinAssembly(Lane.Project), .. arguments], Lane.Project.Parent, environment ?? Environment);

    private DemoRun Execute(string label, LaneRuntime runtime, string fileName, IReadOnlyList<string> arguments, string workingDirectory, IReadOnlyDictionary<string, string>? environment)
    {
        var started = DateTimeOffset.UtcNow;
        var result = Shell.Run(fileName, arguments, workingDirectory, environment ?? Environment, s_runTimeout);
        var run = new DemoRun(label, runtime, result, started, DateTimeOffset.UtcNow);
        Log.Information("  {Label}: exit {Exit} after {Seconds:0.0}s", label, result.ExitCode, result.Elapsed.TotalSeconds);
        return run;
    }
}
