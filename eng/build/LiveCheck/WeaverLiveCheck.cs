using System.Net.Sockets;
using System.Text.RegularExpressions;

namespace Qyl.Build;

/// <summary>
/// What the instrumentation emits, judged against the pinned semantic-convention registry rather than
/// against an assertion written in this repository. <c>weaver registry live-check</c> listens for
/// OTLP/gRPC and judges every span, metric and resource it receives; one demo lane per row of the
/// native-source table exports into it.
///
/// A finding is never waved through: <c>--fail-on violation</c> is weaver's own threshold, and a
/// violation is closed by changing what the instrumentation writes or by declaring the key in the
/// registry. How a finding is levelled is the registry's decision: <c>--config .weaver.toml</c> drops
/// weaver's built-in deprecated/type/enum advisors by finding id, and <c>--advice-policies</c>
/// re-issues them at the level the registry chose. Both halves are checked before weaver starts,
/// because weaver reports neither a missing config nor an unreadable policy directory.
/// </summary>
static partial class WeaverLiveCheck
{
    private const string RegistryRepository = "https://github.com/ANcpLua/Qyl.Telemetry.SemanticConventions.git";
    private const string SemconvPackage = "Qyl.Telemetry.SemanticConventions";

    // AddQyl's exporter speaks gRPC to port 4317 and OTLP/HTTP to any other, and weaver listens for
    // gRPC only, so the listener takes the conventional gRPC port.
    private const int OtlpGrpcPort = 4317;

    // Bounds one demo lane end to end: wake, build, NativeAOT publish and runs.
    private static readonly TimeSpan s_laneTimeout = TimeSpan.FromMinutes(20);

    // weaver's inactivity timer resets on every span it receives, so the longest silence it has to
    // survive is one whole cold lane. Larger than the lane bound, so the lane bound is the enforcer and
    // weaver never stops mid-run.
    private static readonly TimeSpan s_inactivity = s_laneTimeout + TimeSpan.FromMinutes(2);

    /// <summary>One lane per row of the native-source table. RabbitMQ's two source names share one lane;
    /// Elastic.Transport and Elasticsearch share one source and have a lane each, because the two
    /// clients put different attributes on it.</summary>
    public static readonly string[] Lanes = ["azure", "corewcf", "elastictransport", "elasticsearch", "masstransit", "mongodb", "nservicebus", "quartz", "rabbitmq"];

    public static AbsolutePath Checkout => Repo.Root / ".semconv";

    public static void Run(IReadOnlyCollection<string> skipped)
    {
        var unknown = skipped.Except(Lanes, StringComparer.Ordinal).ToArray();
        Gate.Require(unknown.Length == 0, $"unknown live-check lane(s): {string.Join(", ", unknown)}; lanes: {string.Join(", ", Lanes)}");

        var registry = PrepareRegistry();
        var weaver = ResolveWeaver();
        var lanes = Lanes.Where(lane => !skipped.Contains(lane)).Select(DemoLanes.ByName).ToArray();
        var testbed = lanes.Any(static lane => lane.Services.Count > 0) ? Testbed.Load() : null;
        Gate.Require(QylCollectorPortFree(OtlpGrpcPort), $"port {OtlpGrpcPort} is taken; weaver needs it for the OTLP/gRPC listener (stop the process holding it, e.g. a running `qyl up`)");

        var adminPort = QylCollector.FreePort();
        string[] arguments =
        [
            "registry", "live-check",
            "--registry", registry / "registry",
            "--config", registry / "registry" / ".weaver.toml",
            "--advice-policies", registry / "registry" / "policies" / "live_check_advice",
            // Core and genai are dependency registries; without this every key qyl does not redeclare reads as unknown.
            "--include-unreferenced",
            "--otlp-grpc-port", OtlpGrpcPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--admin-port", adminPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--inactivity-timeout", ((int)s_inactivity.TotalSeconds).ToString(System.Globalization.CultureInfo.InvariantCulture),
            "--fail-on", "violation",
        ];
        Log.Information("{Weaver} {Arguments}", weaver, Shell.Quote(arguments));
        using var listener = RunningProcess.Start(weaver, arguments, registry, Shell.CleanEnvironment(), echo: true);
        var endpoint = new Uri($"http://127.0.0.1:{OtlpGrpcPort}");
        var stopped = false;
        try
        {
            WaitForListener(listener);
            foreach (var lane in lanes)
            {
                Log.Information("== {Lane} live-check lane ==", lane.Name);
                EnsureAlive(listener, $"before the {lane.Name} lane");
                foreach (var service in lane.Services)
                    testbed!.Wake(service);
                var started = DateTime.UtcNow;
                lane.Run(new LaneContext(lane, endpoint, new WeaverJudgesTelemetry(), testbed));
                Gate.Require(DateTime.UtcNow - started < s_laneTimeout, $"{lane.Name} lane took longer than {s_laneTimeout.TotalMinutes:0} minutes");
                EnsureAlive(listener, $"during the {lane.Name} lane");
            }

            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
            using var response = http.PostAsync($"http://127.0.0.1:{adminPort}/stop", null).GetAwaiter().GetResult();
            Gate.Require(response.IsSuccessStatusCode, $"weaver admin port {adminPort} did not accept /stop: {(int)response.StatusCode}");
            stopped = true;
        }
        finally
        {
            if (!stopped)
                listener.Kill();
        }

        var result = listener.WaitForExit(TimeSpan.FromMinutes(2));
        Gate.Require(result.ExitCode == 0, $"live check reported findings at or above violation: exit={result.ExitCode}");
        if (skipped.Count > 0)
            Log.Information("live-check-partial-ok skipped={Skipped}", string.Join(",", skipped.Order(StringComparer.Ordinal)));
        else
            Log.Information("live-check-ok");
    }

    /// <summary>Clones the registry at SEMCONV_REF, fetches its filtered core, and checks it is the
    /// release whose constants this repository compiles against.</summary>
    private static AbsolutePath PrepareRegistry()
    {
        var reference = SemconvRef();
        Checkout.DeleteDirectory();
        Shell.Checked("git", ["clone", "-q", "--depth", "1", "--branch", reference, RegistryRepository, Checkout], Repo.Root);
        MaterializeCore();

        Gate.Require((Checkout / "registry" / "manifest.yaml").FileExists(), $"{Repo.Relative(Checkout)} has no registry/manifest.yaml");
        var pinned = Repo.Pin(SemconvPackage);
        var checkedOut = VersionPrefix().Match(File.ReadAllText(Checkout / "Directory.Build.props")) is { Success: true } match ? match.Groups[1].Value : null;
        // Judging spans against a registry other than the one that produced the constants is the drift
        // that would look like a green gate.
        Gate.Require(pinned == checkedOut, $"the {reference} registry is {checkedOut}, but Directory.Packages.props pins {SemconvPackage} {pinned}");
        Gate.Require((Checkout / ".build" / "core-filtered" / "model").DirectoryExists(), "the filtered core registry is missing at .build/core-filtered/model");

        var policies = Checkout / "registry" / "policies" / "live_check_advice";
        var rego = policies.DirectoryExists() ? policies.GlobFiles("*.rego").Select(static file => file.Name).Order(StringComparer.Ordinal).ToArray() : [];
        Gate.Require(rego.Length > 0, $"the {checkedOut} registry ships no advice policies at registry/policies/live_check_advice");
        Gate.Require((Checkout / "registry" / ".weaver.toml").FileExists(), $"the {checkedOut} registry ships no registry/.weaver.toml, so the built-in advisors would stand");
        Log.Information("registry {Version} at {Reference}: {Checkout}; advice policies: {Policies}", checkedOut, reference, Repo.Relative(Checkout), string.Join(", ", rego));
        return Checkout;
    }

    public static string SemconvRef()
    {
        var match = SemconvRefLine().Match(File.ReadAllText(Repo.LiveCheckEnv));
        Gate.Require(match.Success, $"{Repo.Relative(Repo.LiveCheckEnv)} sets no SEMCONV_REF");
        return match.Groups[1].Value;
    }

    /// <summary>weaver from PATH, else the version mise.toml pins.</summary>
    private static string ResolveWeaver()
    {
        foreach (var directory in (Environment.GetEnvironmentVariable("PATH") ?? string.Empty).Split(Path.PathSeparator))
        {
            var candidate = Path.Combine(directory, "weaver");
            if (File.Exists(candidate))
                return candidate;
        }

        var mise = Shell.Run("mise", ["which", "weaver"], Repo.Root);
        var resolved = mise.Stdout.Trim();
        Gate.Require(mise.ExitCode == 0 && File.Exists(resolved), $"weaver is neither on PATH nor installed by mise (run `mise install`): {mise.Stderr.Trim()}");
        return resolved;
    }

    private static void WaitForListener(RunningProcess listener)
    {
        var deadline = DateTime.UtcNow.AddSeconds(60);
        while (DateTime.UtcNow < deadline)
        {
            Gate.Require(!listener.HasExited, () => $"weaver exited before it opened port {OtlpGrpcPort}\nstdout={listener.Stdout}\nstderr={listener.Stderr}");
            if (!QylCollectorPortFree(OtlpGrpcPort))
                return;
            Thread.Sleep(500);
        }

        throw Gate.Fail($"weaver did not open port {OtlpGrpcPort} within 60s");
    }

    /// <summary>A listener that has exited leaves the port closed, and a demo exporting into a closed
    /// port still passes its own assertions — so every lane after the exit would be judged by nobody.</summary>
    private static void EnsureAlive(RunningProcess listener, string where) =>
        Gate.Require(!listener.HasExited, () => $"weaver stopped listening {where}; every span after that point was judged by nobody\nstdout={listener.Stdout}\nstderr={listener.Stderr}");

    private static bool QylCollectorPortFree(int port)
    {
        try
        {
            using var client = new TcpClient();
            client.Connect("127.0.0.1", port);
            return false;
        }
        catch (SocketException)
        {
            return true;
        }
    }

    [GeneratedRegex(@"^\s*SEMCONV_REF\s*=\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex SemconvRefLine();

    [GeneratedRegex(@"<VersionPrefix[^>]*>([^<]+)</VersionPrefix>")]
    private static partial Regex VersionPrefix();
}

/// <summary>
/// The filtered core registry the qyl manifest depends on, materialized the way the registry's own
/// scripts/fetch-core.sh does it — that script needs a Python interpreter with PyYAML, which this
/// build does not use. The core tag is read from the manifest's <c>core</c> dependency (its
/// schema_url names the version); gen-ai/, mcp/, openai/ and the registry.aws.bedrock group are
/// removed, because a direct unfiltered core dependency collides with the genai dependency.
/// </summary>
static partial class WeaverLiveCheck
{
    private const string CoreRemote = "https://github.com/open-telemetry/semantic-conventions.git";
    private const string SchemaPrefix = "https://opentelemetry.io/schemas/";

    private static void MaterializeCore()
    {
        var manifest = Yaml.LoadMapping(Checkout / "registry" / "manifest.yaml");
        var core = (manifest.GetValueOrDefault("dependencies") as List<object?> ?? [])
            .OfType<OrderedDictionary<string, object?>>()
            .FirstOrDefault(static dependency => dependency.GetValueOrDefault("name") as string == "core")
            ?? throw Gate.Fail("registry/manifest.yaml has no 'core' dependency");
        var schemaUrl = core.GetValueOrDefault("schema_url") as string ?? string.Empty;
        Gate.Require(schemaUrl.StartsWith(SchemaPrefix, StringComparison.Ordinal), $"the core schema_url has an unexpected shape: {schemaUrl}");
        var reference = "v" + schemaUrl[SchemaPrefix.Length..];

        var work = Checkout / ".build";
        var repository = work / "semantic-conventions";
        var output = work / "core-filtered";
        repository.DeleteDirectory();
        Shell.Checked("git", ["clone", "-q", "--depth", "1", "--branch", reference, CoreRemote, repository], Repo.Root);
        var commit = Shell.Checked("git", ["-C", repository, "rev-parse", "HEAD"]).Stdout.Trim();

        output.CreateOrCleanDirectory();
        (repository / "model").Copy(output / "model");
        foreach (var required in new[] { "http", "manifest.yaml" })
            Gate.Require(File.Exists(output / "model" / required) || Directory.Exists(output / "model" / required), $"the core semantic-conventions model is missing {required}");
        foreach (var removed in new[] { "gen-ai", "mcp", "openai" })
            (output / "model" / removed).DeleteDirectory();

        var aws = output / "model" / "aws" / "registry.yaml";
        if (aws.FileExists())
        {
            var skip = false;
            var kept = new List<string>();
            foreach (var line in File.ReadAllLines(aws))
            {
                if (line.StartsWith("  - id: ", StringComparison.Ordinal))
                    skip = line == "  - id: registry.aws.bedrock";
                if (!skip)
                    kept.Add(line);
            }

            File.WriteAllLines(aws, kept);
        }

        File.WriteAllText(work / "core-commit.txt", commit + "\n");
        File.WriteAllText(work / "core-ref.txt", reference + "\n");
        Log.Information("core {Reference} ({Commit}) -> {Output}", reference, commit, Repo.Relative(output / "model"));
    }
}
