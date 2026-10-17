using System.Text.Json;
using System.Text.RegularExpressions;

namespace Qyl.Build.AotPublishGate;

/// <summary>One tolerated third-party diagnostic: its id, the assembly that produced it, the package
/// and exact version it ships in, a marker the line must contain, and how often it occurs.</summary>
sealed record AotApproval(string Diagnostic, string Assembly, string Package, string Version, string Marker, int Count);

enum AotVerdict
{
    Ok,
    Regression,
    HardBreak,
    PolicyDrift,
    Promotion,
}

sealed record AotPublishOutcome(int ExitCode, IReadOnlyList<string> Diagnostics, string Tail);

/// <summary>
/// The NativeAOT publish classification. A clean demo publishes with warnings as errors; a vendor-warned
/// demo publishes with warnings relaxed, and then every diagnostic it produced must be one approval
/// below — by id, owning assembly, resolved package and version, marker and count. Nothing qyl owns is
/// ever approved. Every native binary is executed by its demo lane.
/// </summary>
static partial class AotPublish
{
    public static readonly string[] CleanDemos =
    [
        "Qyl.RealAdoNetDemo",
        "Qyl.RealAspNetCoreDemo",
        "Qyl.RealAzureDemo",
        "Qyl.RealElasticTransportDemo",
        "Qyl.RealElasticsearchDemo",
        "Qyl.RealGrpcClientDemo",
        "Qyl.RealHttpClientDemo",
        "Qyl.RealILoggerDemo",
        "Qyl.RealMcpDemo",
        "Qyl.RealMySqlConnectorDemo",
        "Qyl.RealNetRuntimeMetricsDemo",
        "Qyl.RealNpgsqlDemo",
        "Qyl.RealRabbitMqDemo",
        "Qyl.RealRedisDemo",
        "Qyl.RealSessionPropagationDemo",
        "Qyl.RealSqliteDemo",
    ];

    // The runtime packs ship with the SDK, so their version is .NET's and moves with every servicing
    // release; this repository records it nowhere. These two placeholders are filled from the runtime
    // this machine has, measured with `dotnet --list-runtimes`, so the pin stays exact without being
    // frozen. Every other version below is a Directory.Packages.props dependency and stays a literal.
    private const string AspNetCoreRuntime = "{aspnetcore_runtime}";
    private const string NetCoreRuntime = "{netcore_runtime}";

    private static AotApproval Approved(string diagnostic, string assembly, string package, string version, int count = 1, string? marker = null) =>
        new(diagnostic, assembly, package.ToLowerInvariant(), version, marker ?? $"Assembly '{assembly}' produced", count);

    private static readonly AotApproval[] EfCoreApprovals =
    [
        Approved("IL2104", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore", "10.0.12"),
        Approved("IL3053", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore", "10.0.12"),
        Approved("IL2104", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Relational", "10.0.12"),
        Approved("IL3053", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Relational", "10.0.12"),
        Approved("IL2104", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.12"),
        Approved("IL3053", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.12"),
        Approved("IL3002", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.12", 3, "Microsoft.EntityFrameworkCore.Infrastructure.SpatialiteLoader.FindExtension()"),
        Approved("IL3002", "Microsoft.Extensions.DependencyModel", "Microsoft.Extensions.DependencyModel", "10.0.12", marker: "Microsoft.Extensions.DependencyModel.DependencyContext..cctor()"),
    ];

    private static readonly AotApproval[] SqlClientApprovals =
    [
        Approved("IL2104", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.1.1"),
        Approved("IL3053", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.1.1"),
        Approved("IL2104", "Microsoft.Data.SqlClient.Internal.Logging", "Microsoft.Data.SqlClient.Internal.Logging", "7.1.1"),
        Approved("IL2104", "System.Configuration.ConfigurationManager", "System.Configuration.ConfigurationManager", "9.0.18"),
    ];

    public static readonly IReadOnlyDictionary<string, AotApproval[]> VendorWarnedDemos = new Dictionary<string, AotApproval[]>(StringComparer.Ordinal)
    {
        ["Qyl.RealAspNetCoreMetricsDemo"] =
        [
            Approved("IL3053", "Microsoft.AspNetCore.Components.Endpoints", "microsoft.aspnetcore.app.runtime.{rid}", AspNetCoreRuntime),
            Approved("IL2104", "Microsoft.AspNetCore.Components", "microsoft.aspnetcore.app.runtime.{rid}", AspNetCoreRuntime),
            Approved("IL3053", "Microsoft.AspNetCore.Components", "microsoft.aspnetcore.app.runtime.{rid}", AspNetCoreRuntime),
        ],
        ["Qyl.RealEfCoreDemo"] = EfCoreApprovals,
        ["Qyl.RealGraphQlDemo"] =
        [
            Approved("IL2104", "GraphQL", "GraphQL", "8.8.5"),
            Approved("IL3053", "GraphQL", "GraphQL", "8.8.5"),
        ],
        ["Qyl.RealMassTransitDemo"] =
        [
            Approved("IL2104", "MassTransit", "MassTransit", "8.5.11"),
            Approved("IL3053", "MassTransit", "MassTransit", "8.5.11"),
            Approved("IL2104", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.11"),
            Approved("IL3053", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.11"),
            Approved("IL3000", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.11", marker: "MassTransit.Metadata.BusHostInfo."),
        ],
        ["Qyl.RealMySqlDataDemo"] =
        [
            Approved("IL2104", "MySql.Data", "MySql.Data", "26.7.0"),
            Approved("IL2104", "System.Configuration.ConfigurationManager", "System.Configuration.ConfigurationManager", "10.0.8"),
        ],
        ["Qyl.RealNServiceBusDemo"] =
        [
            Approved("IL2104", "NServiceBus.Core", "NServiceBus", "10.2.9"),
            Approved("IL3053", "NServiceBus.Core", "NServiceBus", "10.2.9"),
            Approved("IL3000", "NServiceBus.Core", "NServiceBus", "10.2.9", 2, "NServiceBus.FileVersionRetriever."),
        ],
        ["Qyl.RealOracleMdaDemo"] =
        [
            Approved("IL2104", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.301"),
            Approved("IL3053", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.301"),
            Approved("IL3000", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.301", 5, "Oracle"),
        ],
        ["Qyl.RealSqlClientDemo"] = SqlClientApprovals,
        ["Qyl.RealWcfClientDemo"] =
        [
            Approved("IL2104", "System.ServiceModel.Primitives", "System.ServiceModel.Primitives", "10.0.652802"),
            Approved("IL3053", "System.ServiceModel.Primitives", "System.ServiceModel.Primitives", "10.0.652802"),
            Approved("IL3053", "System.Reflection.DispatchProxy", "microsoft.netcore.app.runtime.nativeaot.{rid}", NetCoreRuntime),
            Approved("IL3053", "System.Private.DataContractSerialization", "microsoft.netcore.app.runtime.nativeaot.{rid}", NetCoreRuntime),
        ],
    };

    /// <summary>Policies for projects that are not demos: the web API AOT consumer carries the EF Core
    /// and SqlClient vendor boundaries.</summary>
    private static readonly IReadOnlyDictionary<string, AotApproval[]> ExternalWarnedProjects = new Dictionary<string, AotApproval[]>(StringComparer.Ordinal)
    {
        ["WebApiAotDemo"] = [.. EfCoreApprovals, .. SqlClientApprovals],
    };

    /// <summary>Extra publish properties a demo needs: EF Core publishes its compiled model.</summary>
    private static readonly IReadOnlyDictionary<string, string[]> ExtraProperties = new Dictionary<string, string[]>(StringComparer.Ordinal)
    {
        ["Qyl.RealEfCoreDemo"] = ["-p:QylEfCoreUseCompiledModel=true"],
    };

    private static readonly HashSet<string> PublishedThisRun = new(StringComparer.Ordinal);
    private static bool _generatorPrebuilt;
    private static IReadOnlyDictionary<string, string>? _runtimeVersions;

    /// <summary>
    /// Builds the source generator into artifacts/ before any NativeAOT publish: under PublishAot a demo
    /// consumes it as the prebuilt analyzer from that path, and on a fresh checkout nothing else has put
    /// it there.
    /// </summary>
    public static void PrebuildGenerator()
    {
        if (_generatorPrebuilt)
            return;
        DotNet.Build(Repo.GeneratorProject, ["--disable-build-servers"]);
        _generatorPrebuilt = true;
    }

    /// <summary>A demo's NativeAOT binary, published the way its classification says, once per build
    /// run. A clean demo must publish warning-free; a vendor-warned one must match its policy exactly;
    /// a vendor-warned demo that became clean fails too, so its boundary gets removed.</summary>
    public static string PublishDemo(string demo)
    {
        var project = Repo.DemoProject(demo);
        var executable = Path.Combine(Repo.PublishDirectory(project, "nativeaot"), Repo.ExecutableName(demo));
        if (PublishedThisRun.Contains(demo) && File.Exists(executable))
            return executable;

        var (verdict, detail) = CleanDemos.Contains(demo, StringComparer.Ordinal) ? CheckClean(demo)
            : VendorWarnedDemos.ContainsKey(demo) ? CheckWarned(demo)
            : throw Gate.Fail($"{demo} is in neither NativeAOT classification");
        Gate.Require(verdict is AotVerdict.Ok, $"NativeAOT publish of {demo}: {verdict} — {detail}");
        Gate.Require(File.Exists(executable), $"NativeAOT executable missing: {executable}");
        return executable;
    }

    public static (AotVerdict Verdict, string Detail) CheckClean(string demo)
    {
        var outcome = PublishClassified(demo, strict: true);
        return outcome is { ExitCode: 0, Diagnostics.Count: 0 }
            ? (AotVerdict.Ok, "warning-clean")
            : (AotVerdict.Regression, $"exit={outcome.ExitCode}; " + (outcome.Diagnostics.FirstOrDefault() ?? outcome.Tail));
    }

    public static (AotVerdict Verdict, string Detail) CheckWarned(string demo)
    {
        var outcome = PublishClassified(demo, strict: false);
        if (outcome.ExitCode != 0)
            return (AotVerdict.HardBreak, $"exit={outcome.ExitCode} even with warnings relaxed; " + (outcome.Diagnostics.FirstOrDefault() ?? outcome.Tail));
        if (outcome.Diagnostics.Count == 0)
            return (AotVerdict.Promotion, "now warning-clean upstream — move it to CleanDemos");
        var (valid, detail) = ValidateWarningPolicy(demo, Repo.DemoProject(demo), outcome.Diagnostics);
        return (valid ? AotVerdict.Ok : AotVerdict.PolicyDrift, detail);
    }

    private static AotPublishOutcome PublishClassified(string demo, bool strict)
    {
        var project = Repo.DemoProject(demo);
        Gate.Require(project.FileExists(), $"demo project not found: {project}");
        PrebuildGenerator();
        var outcome = Publish(project, Repo.PublishDirectory(project, "nativeaot"), strict, ExtraProperties.GetValueOrDefault(demo));
        if (outcome.ExitCode == 0)
            PublishedThisRun.Add(demo);
        return outcome;
    }

    // ILC and the SDK occasionally die on a signal; that is a toolchain crash, not an AOT regression.
    // A signal death is retried; a deterministic crash still fails every attempt.
    private const int SignalRetries = 2;

    public static AotPublishOutcome Publish(AbsolutePath project, AbsolutePath output, bool strict, IEnumerable<string>? extraProperties = null, IReadOnlyDictionary<string, string>? environment = null)
    {
        string[] common = ["-c", "Release", "-r", Repo.RuntimeIdentifier, "-p:PublishAot=true", "--disable-build-servers", "-maxcpucount:4", "-nodeReuse:false"];
        string[] extra = [.. extraProperties ?? []];
        string[][] commands =
        [
            ["build", project, "--no-incremental", .. common, "-p:SelfContained=true", "-p:TreatWarningsAsErrors=true", "-v", "quiet", .. extra],
            ["publish", project, .. common, "--self-contained", "true", $"-p:TreatWarningsAsErrors={(strict ? "true" : "false")}", "-o", output, "-clp:NoSummary", "-v", "minimal", .. extra],
        ];

        ProcessResult? result = null;
        for (var attempt = 0; attempt <= SignalRetries; attempt++)
        {
            foreach (var command in commands)
            {
                result = Shell.Run("dotnet", command, Repo.Root, environment);
                if (result.ExitCode != 0)
                    break;
            }

            if (!IsSignalDeath(result!.ExitCode))
                break;
            Log.Warning("{Project} NativeAOT publish killed by a signal (exit {Exit}), attempt {Attempt}/{Total}", project.NameWithoutExtension, result.ExitCode, attempt + 1, SignalRetries + 1);
        }

        var lines = (result!.Stdout + "\n" + result.Stderr).Split('\n');
        var diagnostics = lines.Where(static line => DiagnosticLine().IsMatch(line)).Select(static line => line.Trim()).ToArray();
        var errors = lines.Where(static line => ErrorLine().IsMatch(line)).Select(static line => line.Trim()).ToArray();
        var tail = errors.LastOrDefault() ?? lines.Select(static line => line.Trim()).LastOrDefault(static line => line.Length > 0) ?? "(no output)";
        return new AotPublishOutcome(result.ExitCode, diagnostics, tail.Length > 240 ? tail[..240] : tail);
    }

    private static bool IsSignalDeath(int exitCode) => exitCode is 134 or 137 or 139 || exitCode < 0;

    public static (bool Valid, string Detail) ValidateWarningPolicy(string name, AbsolutePath project, IReadOnlyList<string> diagnostics)
    {
        var policy = VendorWarnedDemos.GetValueOrDefault(name) ?? ExternalWarnedProjects.GetValueOrDefault(name);
        if (policy is null)
            return (false, $"no vendor-warning policy for {name}");

        var rid = Repo.RuntimeIdentifier;
        var runtime = RuntimePackVersions();
        string Resolve(string value) => value
            .Replace("{rid}", rid, StringComparison.Ordinal)
            .Replace(AspNetCoreRuntime, runtime["aspnetcore_runtime"], StringComparison.Ordinal)
            .Replace(NetCoreRuntime, runtime["netcore_runtime"], StringComparison.Ordinal);

        var expected = new Dictionary<(string, string, string, string), int>();
        foreach (var approval in policy)
        {
            var key = (approval.Diagnostic, approval.Assembly, Resolve(approval.Package), Resolve(approval.Version));
            expected[key] = expected.GetValueOrDefault(key) + approval.Count;
        }

        var actual = new Dictionary<(string, string, string, string), int>();
        var resolved = ResolvedPackages(project, diagnostics);
        foreach (var line in diagnostics)
        {
            var match = DiagnosticLine().Match(line);
            var diagnostic = match.Success ? match.Groups[1].Value.ToUpperInvariant() : "UNKNOWN";
            if (line.Contains("Assembly 'Qyl.", StringComparison.Ordinal) || line.Replace('\\', '/').Contains("/src/qyl.telemetry.", StringComparison.OrdinalIgnoreCase))
                return (false, $"a qyl-owned diagnostic is never approved: {line}");

            var matches = policy.Where(approval => approval.Diagnostic == diagnostic && line.Contains(approval.Marker, StringComparison.Ordinal)).ToArray();
            if (matches.Length != 1)
                return (false, $"unapproved or ambiguous diagnostic: {line}");

            var approval = matches[0];
            var package = Resolve(approval.Package);
            var version = Resolve(approval.Version);
            var path = PackagePath().Match(line.Replace('\\', '/'));
            if (path.Success && (path.Groups[1].Value.ToLowerInvariant(), path.Groups[2].Value) != (package, version))
                return (false, $"package drift for {diagnostic}/{approval.Assembly}: {path.Groups[1].Value}@{path.Groups[2].Value} != {package}@{version}");

            var key = (diagnostic, approval.Assembly, package, version);
            actual[key] = actual.GetValueOrDefault(key) + 1;
        }

        var missingPackages = expected.Keys.Select(static key => (key.Item3, key.Item4)).Distinct().Where(package => !resolved.Contains(package)).OrderBy(static package => package.Item1, StringComparer.Ordinal).ToArray();
        if (missingPackages.Length > 0)
            return (false, "approved package/version not resolved: " + string.Join(", ", missingPackages.Select(static package => $"{package.Item1}@{package.Item2}")));

        var drift = expected.Keys.Union(actual.Keys)
            .Where(key => expected.GetValueOrDefault(key) != actual.GetValueOrDefault(key))
            .Select(key => $"{key.Item1}/{key.Item2} {key.Item3}@{key.Item4}: expected {expected.GetValueOrDefault(key)}, got {actual.GetValueOrDefault(key)}")
            .ToArray();
        if (drift.Length > 0)
            return (false, "warning policy drift: " + string.Join("; ", drift));

        var assemblies = actual.Keys.Select(static key => key.Item2).Distinct().OrderBy(static name => name, StringComparer.Ordinal);
        return (true, $"{actual.Values.Sum()} approved diagnostic(s) from {string.Join(", ", assemblies)}");
    }

    /// <summary>The installed runtime version per family, in the major global.json's SDK targets:
    /// measured, because a publish resolves the runtime pack of the runtime it runs on.</summary>
    private static IReadOnlyDictionary<string, string> RuntimePackVersions()
    {
        if (_runtimeVersions is not null)
            return _runtimeVersions;

        using var globalJson = JsonDocument.Parse(File.ReadAllText(Repo.Root / "global.json"));
        var major = globalJson.RootElement.GetProperty("sdk").GetProperty("version").GetString()!.Split('.')[0];
        var listing = Shell.Checked("dotnet", ["--list-runtimes"]);
        var families = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["Microsoft.AspNetCore.App"] = "aspnetcore_runtime",
            ["Microsoft.NETCore.App"] = "netcore_runtime",
        };
        var versions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var line in listing.Stdout.Split('\n'))
        {
            var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2 || !families.TryGetValue(parts[0], out var key) || !parts[1].StartsWith(major + ".", StringComparison.Ordinal))
                continue;
            if (!versions.TryGetValue(key, out var current) || CompareVersions(parts[1], current) > 0)
                versions[key] = parts[1];
        }

        var missing = families.Values.Where(key => !versions.ContainsKey(key)).ToArray();
        Gate.Require(missing.Length == 0, $"no {major}.x runtime installed for {string.Join(", ", missing)}; the AOT gate cannot pin the runtime packs");
        return _runtimeVersions = versions;
    }

    private static int CompareVersions(string left, string right)
    {
        static int[] Parts(string version) => version.Split('-', 2)[0].Split('.').Select(static part => int.TryParse(part, out var number) ? number : -1).ToArray();
        var (a, b) = (Parts(left), Parts(right));
        for (var index = 0; index < Math.Max(a.Length, b.Length); index++)
        {
            var comparison = (index < a.Length ? a[index] : 0).CompareTo(index < b.Length ? b[index] : 0);
            if (comparison != 0)
                return comparison;
        }

        return 0;
    }

    private static HashSet<(string, string)> ResolvedPackages(AbsolutePath project, IReadOnlyList<string> diagnostics)
    {
        var packages = new HashSet<(string, string)>();
        AbsolutePath[] candidates = [Repo.Artifacts / "obj" / project.NameWithoutExtension / "project.assets.json", project.Parent / "obj" / "project.assets.json"];
        var assets = candidates.FirstOrDefault(static candidate => candidate.FileExists());
        if (assets is not null)
        {
            using var document = JsonDocument.Parse(File.ReadAllText(assets));
            foreach (var library in document.RootElement.GetProperty("libraries").EnumerateObject())
            {
                if (library.Value.TryGetProperty("type", out var type) && type.GetString() == "package")
                {
                    var separator = library.Name.LastIndexOf('/');
                    packages.Add((library.Name[..separator].ToLowerInvariant(), library.Name[(separator + 1)..]));
                }
            }
        }

        foreach (var line in diagnostics)
        {
            var match = PackagePath().Match(line.Replace('\\', '/'));
            if (match.Success)
                packages.Add((match.Groups[1].Value.ToLowerInvariant(), match.Groups[2].Value));
        }

        return packages;
    }

    [GeneratedRegex(@"\b(?:warning|error)\s+([A-Z]+\d+)\b", RegexOptions.IgnoreCase)]
    private static partial Regex DiagnosticLine();

    [GeneratedRegex(@"/(?:\.nuget/packages|packages)/([^/]+)/([^/]+)/", RegexOptions.IgnoreCase)]
    private static partial Regex PackagePath();

    [GeneratedRegex(@": error |\berror IL\d|\berror CS\d|\berror MSB\d|\berror NETSDK\d")]
    private static partial Regex ErrorLine();
}
