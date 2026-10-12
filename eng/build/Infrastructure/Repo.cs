using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace Qyl.Build;

/// <summary>Every path, version and platform fact the gates share. Nothing here is typed twice.</summary>
static partial class Repo
{
    public static AbsolutePath Root => FalloutBuild.RootDirectory;
    public static AbsolutePath Src => Root / "src";
    public static AbsolutePath DemosDirectory => Root / "demos";
    public static AbsolutePath ContractsDirectory => Root / "contracts";
    public static AbsolutePath Artifacts => Root / "artifacts";
    public static AbsolutePath Fixtures => Root / "eng" / "fixtures";
    public static AbsolutePath Solution => Root / "Qyl.Telemetry.AutoInstrumentation.slnx";
    public static AbsolutePath DemoSolution => Root / "Qyl.Telemetry.AutoInstrumentation.Demos.slnx";
    public static AbsolutePath BuildProps => Root / "Directory.Build.props";
    public static AbsolutePath PackagesProps => Root / "Directory.Packages.props";
    public static AbsolutePath LiveCheckEnv => Root / "tools" / "live-check.env";
    public static AbsolutePath ToolManifest => Root / ".config" / "dotnet-tools.json";

    public const string TargetFramework = "net10.0";
    public const string NuGetOrg = "https://api.nuget.org/v3/index.json";

    public static AbsolutePath SourceProject(string name) => Src / name / $"{name}.csproj";
    public static AbsolutePath DemoProject(string name) => DemosDirectory / name / $"{name}.csproj";

    public static AbsolutePath CoreProject => SourceProject("Qyl.Telemetry.AutoInstrumentation");
    public static AbsolutePath GeneratorProject => SourceProject("Qyl.Telemetry.AutoInstrumentation.SourceGenerators");
    public static AbsolutePath DiagnosticListenersProject => SourceProject("Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners");
    public static AbsolutePath AutoHostingProject => SourceProject("Qyl.Telemetry.AutoInstrumentation.Hosting");
    public static AbsolutePath HostingProject => SourceProject("Qyl.Telemetry.Hosting");
    public static AbsolutePath CoreBuildTargets => Src / "Qyl.Telemetry.AutoInstrumentation" / "buildTransitive" / "Qyl.Telemetry.AutoInstrumentation.targets";

    /// <summary>The generator as a prebuilt analyzer: what a NativeAOT publish consumes in place of the
    /// project reference. A Release build of the generator project puts it here.</summary>
    public static AbsolutePath GeneratorAssembly =>
        Artifacts / "bin" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "release" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.dll";

    /// <summary>Every project under src/ that ships: the release list is derived, never typed.</summary>
    public static IReadOnlyList<AbsolutePath> PackableProjects =>
        Src.GlobFiles("*/*.csproj")
            .Where(static project => !File.ReadAllText(project).Contains("<IsPackable>false</IsPackable>", StringComparison.Ordinal))
            .OrderBy(static project => project.ToString(), StringComparer.Ordinal)
            .ToArray();

    public static string Version
    {
        get
        {
            var match = VersionPattern().Match(File.ReadAllText(BuildProps));
            Gate.Require(match.Success, "Directory.Build.props declares no <Version>");
            return match.Groups[1].Value.Trim();
        }
    }

    /// <summary>The Directory.Packages.props pins; a range pin resolves to its lower bound, which is
    /// what NuGet restores.</summary>
    public static IReadOnlyDictionary<string, string> PackagePins
    {
        get
        {
            var pins = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (Match match in PackageVersionPattern().Matches(File.ReadAllText(PackagesProps)))
                pins[match.Groups[1].Value] = match.Groups[2].Value;
            return pins;
        }
    }

    public static string Pin(string packageId)
    {
        Gate.Require(PackagePins.TryGetValue(packageId, out var version), $"Directory.Packages.props pins no {packageId}");
        return version!.StartsWith('[') ? version[1..].Split(',')[0] : version;
    }

    public static string RuntimeIdentifier
    {
        get
        {
            var arm = RuntimeInformation.OSArchitecture is Architecture.Arm64;
            if (OperatingSystem.IsMacOS()) return arm ? "osx-arm64" : "osx-x64";
            if (OperatingSystem.IsLinux()) return arm ? "linux-arm64" : "linux-x64";
            if (OperatingSystem.IsWindows()) return arm ? "win-arm64" : "win-x64";
            throw Gate.Fail($"unsupported platform: {RuntimeInformation.OSDescription} {RuntimeInformation.OSArchitecture}");
        }
    }

    public static string ExecutableName(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>Where a Release build of a repository project puts its assembly (Directory.Build.props
    /// routes every project into artifacts/).</summary>
    public static AbsolutePath BinAssembly(AbsolutePath project) =>
        Artifacts / "bin" / project.NameWithoutExtension / "release" / $"{project.NameWithoutExtension}.dll";

    public static AbsolutePath PublishDirectory(AbsolutePath project, string name) =>
        Artifacts / "publish" / project.NameWithoutExtension / "release" / name;

    public static string Relative(AbsolutePath path) =>
        Path.GetRelativePath(Root, path).Replace('\\', '/');

    [GeneratedRegex(@"<Version>\s*([^<]+?)\s*</Version>")]
    private static partial Regex VersionPattern();

    [GeneratedRegex("<PackageVersion\\s+Include=\"([^\"]+)\"\\s+Version=\"([^\"]+)\"")]
    private static partial Regex PackageVersionPattern();
}
