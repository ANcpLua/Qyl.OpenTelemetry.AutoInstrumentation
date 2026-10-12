using System.Text;

namespace Qyl.Build;

/// <summary>
/// The packages this checkout would ship, packed once per build into artifacts/fixtures/feed. A
/// fixture consumer restores them from that folder alone: the same id and version can already be on
/// nuget.org with other content, so package source mapping pins the qyl ids to the local feed.
/// </summary>
static class LocalFeed
{
    private static bool s_packed;

    public static AbsolutePath Directory => FixtureWorkspace.Root / "feed";

    /// <summary>Packs every packable src project at the Directory.Build.props version; returns that version.</summary>
    public static string EnsurePacked()
    {
        var version = Repo.Version;
        if (s_packed)
            return version;

        Directory.CreateOrCleanDirectory();
        foreach (var project in Repo.PackableProjects)
            DotNet.Pack(project, Directory);
        s_packed = true;
        Log.Information("local feed: {Count} packages at {Version} in {Feed}", Repo.PackableProjects.Count, version, Repo.Relative(Directory));
        return version;
    }
}

/// <summary>
/// Isolated consumer projects under artifacts/fixtures. The repository's Directory.Build.props,
/// Directory.Packages.props and nuget.config stop above this folder, so a consumer sees exactly what a
/// NuGet consumer sees; its packages restore into a folder of their own, never into the machine's
/// global cache, where a nuget.org copy of the same version may already sit.
/// </summary>
static class FixtureWorkspace
{
    private static readonly HashSet<string> s_prepared = new(StringComparer.Ordinal);

    public static AbsolutePath Root => Repo.Artifacts / "fixtures";

    /// <summary>The packages this checkout ships. In published mode they come from nuget.org instead.</summary>
    public static readonly string[] LocalPackagePatterns =
    [
        "Qyl.Telemetry.AutoInstrumentation",
        "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners",
        "Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore",
        "Qyl.Telemetry.AutoInstrumentation.Hosting",
        "Qyl.Telemetry.AutoInstrumentation.SqlClient",
        "Qyl.Telemetry.Hosting",
    ];

    /// <summary>A clean directory for one consumer. <paramref name="published"/> restores the qyl
    /// packages from nuget.org rather than from the local feed.</summary>
    public static AbsolutePath Create(string name, bool published = false)
    {
        var scope = Prepare(published);
        var directory = scope / "work" / name;
        directory.CreateOrCleanDirectory();
        return directory;
    }

    /// <summary>Copies a checked-in fixture from eng/fixtures/<paramref name="fixture"/> into <paramref name="destination"/>.</summary>
    public static void CopyFixture(string fixture, AbsolutePath destination, params string[] files)
    {
        var source = Repo.Fixtures / fixture;
        Gate.Require(source.DirectoryExists(), $"missing fixture {Repo.Relative(source)}");
        foreach (var file in files)
        {
            var from = source / file;
            Gate.Require(from.FileExists(), $"missing fixture file {Repo.Relative(from)}");
            (destination / file).Parent.CreateDirectory();
            File.Copy(from, destination / file, overwrite: true);
        }
    }

    public static string ReadFixture(string fixture, string file)
    {
        var path = Repo.Fixtures / fixture / file;
        Gate.Require(path.FileExists(), $"missing fixture file {Repo.Relative(path)}");
        return File.ReadAllText(path);
    }

    private static AbsolutePath Prepare(bool published)
    {
        var scope = published ? Repo.Artifacts / "fixtures-published" : Root;
        if (!s_prepared.Add(scope))
            return scope;

        scope.CreateDirectory();
        File.WriteAllText(scope / "Directory.Build.props", "<Project />\n");
        File.WriteAllText(scope / "Directory.Build.targets", "<Project />\n");
        File.WriteAllText(scope / "Directory.Packages.props", "<Project>\n  <PropertyGroup>\n    <ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally>\n  </PropertyGroup>\n</Project>\n");

        var packages = scope / "packages";
        packages.CreateDirectory();
        // The qyl ids are rebuilt by every run under one version number; only third-party packages
        // may be reused from an earlier run.
        foreach (var pattern in LocalPackagePatterns)
            (packages / pattern.ToLowerInvariant()).DeleteDirectory();

        var config = new StringBuilder()
            .AppendLine("<?xml version=\"1.0\" encoding=\"utf-8\"?>")
            .AppendLine("<configuration>")
            .AppendLine("  <config>")
            .AppendLine($"    <add key=\"globalPackagesFolder\" value=\"{packages}\" />")
            .AppendLine("  </config>")
            .AppendLine("  <packageSources>")
            .AppendLine("    <clear />");
        if (!published)
            config.AppendLine($"    <add key=\"qyl-local\" value=\"{LocalFeed.Directory}\" />");
        config.AppendLine($"    <add key=\"nuget.org\" value=\"{Repo.NuGetOrg}\" />")
            .AppendLine("  </packageSources>")
            .AppendLine("  <packageSourceMapping>");
        if (!published)
        {
            config.AppendLine("    <packageSource key=\"qyl-local\">");
            foreach (var pattern in LocalPackagePatterns)
                config.AppendLine($"      <package pattern=\"{pattern}\" />");
            config.AppendLine("    </packageSource>");
        }

        config.AppendLine("    <packageSource key=\"nuget.org\">")
            .AppendLine("      <package pattern=\"*\" />")
            .AppendLine("    </packageSource>")
            .AppendLine("  </packageSourceMapping>")
            .AppendLine("</configuration>");
        File.WriteAllText(scope / "nuget.config", config.ToString());
        return scope;
    }
}
