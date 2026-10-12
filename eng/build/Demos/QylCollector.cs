using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Qyl.Api.Contracts;
using Qyl.Api.Contracts.Health;

namespace Qyl.Build;

/// <summary>
/// The local qyl collector the demo lanes export to. It is the collector the <c>qyl</c> dotnet tool
/// pinned in .config/dotnet-tools.json ships (the one <c>qyl up</c> launches), started once per build
/// run on free loopback ports with a store of its own, unsecured on loopback, with its self-telemetry
/// export switched off the way <c>qyl up</c> does it, and stopped when the run ends.
/// </summary>
sealed class QylCollector : IDisposable
{
    private static QylCollector? s_shared;
    private readonly RunningProcess _process;
    private readonly AbsolutePath _store;

    private QylCollector(RunningProcess process, AbsolutePath store, Uri api, Uri otlp)
    {
        _process = process;
        _store = store;
        Api = api;
        OtlpHttp = otlp;
        Client = new QylCollectorClient(api);
    }

    /// <summary>The query API and dashboard.</summary>
    public Uri Api { get; }

    /// <summary>The OTLP/HTTP base URL a demo receives as QYL_ENDPOINT.</summary>
    public Uri OtlpHttp { get; }

    public QylCollectorClient Client { get; }

    /// <summary>The run's collector, started on first use and reused by every lane after it.</summary>
    public static QylCollector Shared => s_shared ??= Start();

    public static void StopShared()
    {
        s_shared?.Dispose();
        s_shared = null;
    }

    private static QylCollector Start()
    {
        var assembly = ResolveCollectorAssembly();
        // One store per run: an earlier run's telemetry must not answer this run's queries.
        var store = Repo.Artifacts / "qyl-collector";
        store.CreateOrCleanDirectory();
        var apiPort = FreePort();
        var otlpPort = FreePort();
        var environment = Shell.CleanEnvironment().With(new Dictionary<string, string>
        {
            ["QYL_PORT"] = apiPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["QYL_OTLP_PORT"] = otlpPort.ToString(System.Globalization.CultureInfo.InvariantCulture),
            ["QYL_GRPC_PORT"] = "0",
            ["QYL_BIND_ADDRESS"] = "127.0.0.1",
            ["QYL_DATA_PATH"] = store / "qyl.duckdb",
            ["QYL_OTLP_AUTH_MODE"] = "Unsecured",
            // qyl up blanks this for a collector that must export nowhere; a collector reporting into
            // itself or into another local qyl would mix its own telemetry into what the lanes assert.
            ["OTEL_EXPORTER_OTLP_ENDPOINT"] = string.Empty,
        });

        var process = RunningProcess.Start("dotnet", [assembly], store, environment);
        var collector = new QylCollector(process, store, new Uri($"http://127.0.0.1:{apiPort}"), new Uri($"http://127.0.0.1:{otlpPort}"));
        AppDomain.CurrentDomain.ProcessExit += (_, _) => collector.Dispose();

        var deadline = DateTime.UtcNow.AddSeconds(90);
        while (true)
        {
            Gate.Require(!process.HasExited, () => $"the qyl collector exited before it was healthy\nstdout={process.Stdout}\nstderr={process.Stderr}");
            try
            {
                var health = collector.Client.Health();
                if (health.Status is HealthStatus.Healthy)
                {
                    // The client is typed by Qyl.Api.Contracts; a collector built from another contract
                    // revision would answer in shapes these types do not describe.
                    Gate.Require(health.ContractRevision == ContractRevision.Value,
                        $"the collector serves contract {health.ContractRevision}, Qyl.Api.Contracts {ContractRevision.Value}");
                    Log.Information("qyl collector {Version} ready: api {Api}, OTLP/HTTP {Otlp}, store {Store}", ToolVersion(), collector.Api, collector.OtlpHttp, Repo.Relative(store));
                    return collector;
                }
            }
            catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or GateFailure && DateTime.UtcNow < deadline && !process.HasExited)
            {
            }

            Gate.Require(DateTime.UtcNow < deadline, () => $"the qyl collector did not report healthy within 90s\nstdout={process.Stdout}\nstderr={process.Stderr}");
            Thread.Sleep(500);
        }
    }

    public void Dispose()
    {
        Client.Dispose();
        _process.Dispose();
    }

    private static string ToolVersion()
    {
        using var manifest = JsonDocument.Parse(File.ReadAllText(Repo.ToolManifest));
        return manifest.RootElement.GetProperty("tools").GetProperty("qyl").GetProperty("version").GetString()
               ?? throw Gate.Fail(".config/dotnet-tools.json pins no qyl version");
    }

    /// <summary>The collector assembly inside the pinned tool's runtime package, restored by
    /// <c>dotnet tool restore</c> into the NuGet packages folder.</summary>
    private static string ResolveCollectorAssembly()
    {
        Shell.Checked("dotnet", ["tool", "restore"], Repo.Root);
        var version = ToolVersion();
        var rid = Repo.RuntimeIdentifier;
        var packages = Environment.GetEnvironmentVariable("NUGET_PACKAGES") is { Length: > 0 } configured
            ? configured
            : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
        var assembly = Path.Combine(packages, $"qyl.{rid}", version, "tools", Repo.TargetFramework, rid, "collector", "qyl.collector.dll");
        Gate.Require(File.Exists(assembly), $"the qyl {version} tool has no collector for {rid} at {assembly}");
        return assembly;
    }

    public static int FreePort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }
}
