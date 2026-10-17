using System.Text.RegularExpressions;

namespace Qyl.Build.Contracts;

/// <summary>
/// Version truth. The instrumentation-scope version is stamped onto every emitted span and metric and
/// must match the shipped package. Directory.Build.props &lt;Version&gt; is the single source:
/// <list type="bullet">
/// <item>it must be &gt;= the latest stable v* tag;</item>
/// <item>CHANGELOG.md's top entry must be [&lt;that version&gt;] - &lt;date&gt;, no [Unreleased] entry may
/// exist, and the entries must be unique and descending;</item>
/// <item>any version-pinned README package-reference example must equal it;</item>
/// <item>the generated-code ABI anchor and emitted references must match the package major;</item>
/// <item>QylInstrumentation.Version must stay derived from the build, never a semver literal.</item>
/// </list>
/// </summary>
static partial class VersionSync
{
    private static AbsolutePath Changelog => Repo.Root / "CHANGELOG.md";
    private static AbsolutePath Readme => Repo.Root / "README.md";
    private static AbsolutePath Instrumentation => Repo.Src / "Qyl.Telemetry.AutoInstrumentation" / "QylInstrumentation.cs";
    private static AbsolutePath GeneratedCodeAbi => Repo.Src / "Qyl.Telemetry.AutoInstrumentation" / "GeneratedCode" / "QylGeneratedCodeAbi.cs";
    private static AbsolutePath Generator => Repo.Src / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "QylAutoInstrumentationGenerator.cs";

    public static void Verify()
    {
        var version = Repo.Version;
        Log.Information("version-sync: single source of truth = Directory.Build.props <Version> = {Version}", version);
        CheckPropsVersion(version);
        CheckChangelog(version);
        CheckReadme(version);
        CheckGeneratedCodeAbi(version);
        CheckVersionIsDerived();
        Log.Information("version-sync: OK");
    }

    private static (int Major, int Minor, int Patch) Semver(string text)
    {
        var match = SemverPrefix().Match(text.Trim());
        Gate.Require(match.Success, $"version-sync: cannot parse a semver from {ContractValue.Render(text)}");
        return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
    }

    /// <summary>The newest stable v* tag across the whole repository, deliberately not scoped to the
    /// props major: scoping would let a downgrade to an older major slip through.</summary>
    private static (int, int, int)? LatestStableTag()
    {
        var result = Shell.Run("git", ["-C", Repo.Root, "tag", "--list", "v*", "--sort=-v:refname"], Repo.Root);
        if (result.ExitCode != 0)
            return null;
        foreach (var line in result.Stdout.Split('\n'))
        {
            var match = StableTag().Match(line.Trim());
            if (match.Success)
                return (int.Parse(match.Groups[1].Value), int.Parse(match.Groups[2].Value), int.Parse(match.Groups[3].Value));
        }

        return null;
    }

    private static void CheckPropsVersion(string version)
    {
        if (LatestStableTag() is not { } tag)
        {
            Log.Information("  - no stable v* tag reachable; skipping tag-floor comparison");
            return;
        }

        var tagText = $"{tag.Item1}.{tag.Item2}.{tag.Item3}";
        Gate.Require(Semver(version).CompareTo(tag) >= 0,
            $"version-sync: Directory.Build.props <Version> ({version}) is BEHIND the latest release tag v{tagText}. Bump the version to at least {tagText}.");
        Log.Information("  - props version {Version} >= latest tag v{Tag}", version, tagText);
    }

    private static void CheckChangelog(string version)
    {
        var headings = ChangelogHeading().Matches(File.ReadAllText(Changelog)).ToArray();
        Gate.Require(headings.Length > 0, "version-sync: CHANGELOG.md has no '## [version]' heading");
        // A change is recorded under the version that ships it: bump <Version> and open its entry in the
        // same commit, so the built version always has its notes and no [Unreleased] block waits for one.
        var versions = headings.Select(static heading => heading.Groups["version"].Value).ToArray();
        Gate.Require(!versions.Contains("Unreleased", StringComparer.Ordinal),
            $"version-sync: CHANGELOG.md has an [Unreleased] entry. Record the changes under '## [{version}] - YYYY-MM-DD', bumping Directory.Build.props <Version> first if v{version} is already released.");
        var top = headings[0];
        var topVersion = top.Groups["version"].Value;
        Gate.Require(topVersion == version,
            $"version-sync: CHANGELOG.md top entry is [{topVersion}] but Directory.Build.props <Version> is {version}. Add '## [{version}] - YYYY-MM-DD' above it.");
        Gate.Require(top.Groups["date"].Success, $"version-sync: CHANGELOG.md entry [{version}] has no ISO date: expected '## [{version}] - YYYY-MM-DD'");
        Log.Information("  - CHANGELOG top entry [{Version}] - {Date} matches props version", version, top.Groups["date"].Value);

        var duplicates = versions.GroupBy(static value => value, StringComparer.Ordinal).Where(static group => group.Count() > 1).Select(static group => group.Key).ToArray();
        Gate.Require(duplicates.Length == 0, () => $"version-sync: CHANGELOG.md has duplicate release entries: {ContractValue.RenderSorted(duplicates)}");
        var ordered = versions.Select(Semver).ToArray();
        Gate.Require(ordered.SequenceEqual(ordered.OrderByDescending(static value => value)),
            () => $"version-sync: CHANGELOG.md release entries are not in descending order: {ContractValue.Render(versions.ToList())}");
        Log.Information("  - {Count} CHANGELOG release entries unique and descending", versions.Length);
    }

    private static void CheckReadme(string version)
    {
        var references = ReadmeReference().Matches(File.ReadAllText(Readme)).Select(static match => match.Groups[1].Value).ToArray();
        if (references.Length == 0)
        {
            Log.Information("  - README uses the version-agnostic package-install command");
            return;
        }

        var mismatched = references.Where(value => value != version).Distinct(StringComparer.Ordinal).ToArray();
        Gate.Require(mismatched.Length == 0,
            () => $"version-sync: README package-reference example(s) {ContractValue.RenderSorted(mismatched)} != props version {version}. Update the README install snippets to {version}.");
        Log.Information("  - {Count} README example(s) all pinned to {Version}", references.Length, version);
    }

    private static void CheckGeneratedCodeAbi(string version)
    {
        var major = Semver(version).Major;
        var anchor = $"V{major}";
        var declarations = AbiDeclaration().Matches(File.ReadAllText(GeneratedCodeAbi)).Select(static match => (match.Groups[1].Value, match.Groups[2].Value)).ToArray();
        Gate.Require(declarations.SequenceEqual([(anchor, major.ToString())]),
            () => $"version-sync: generated-code ABI declaration must exactly match the package major: expected=[('{anchor}', '{major}')] actual=[{string.Join(", ", declarations.Select(static pair => $"('{pair.Item1}', '{pair.Item2}')"))}]");

        var expectedReference = $"global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylGeneratedCodeAbi.{anchor}";
        foreach (var (label, path) in new[] { ("source generator", Generator), ("generated interceptor snapshot", ContractInvariants.GeneratedInterceptorSnapshotPath) })
        {
            var references = AbiReference().Matches(File.ReadAllText(path)).Select(static match => match.Value).ToHashSet(StringComparer.Ordinal);
            Gate.Require(references.SetEquals([expectedReference]),
                () => $"version-sync: {label} must reference exactly {expectedReference}: actual={ContractValue.RenderSorted(references)}");
        }

        Log.Information("  - package major {Major} exactly matches QylGeneratedCodeAbi.{Anchor}", major, anchor);
    }

    private static void CheckVersionIsDerived()
    {
        var text = File.ReadAllText(Instrumentation);
        Gate.Require(!VersionLiteral().IsMatch(text),
            "version-sync: QylInstrumentation.Version is a hardcoded semver literal again. It must be baked from the build <Version> via the generated QylVersionInfo const, so the scope version cannot drift from the package.");
        Gate.Require(text.Contains("QylVersionInfo.Version", StringComparison.Ordinal),
            "version-sync: QylInstrumentation.Version no longer references the build-generated QylVersionInfo const; the scope version would be uncontrolled. See the GenerateQylVersionInfo target.");
        Log.Information("  - QylInstrumentation.Version is build-derived from generated QylVersionInfo (not a literal)");
    }

    [GeneratedRegex(@"^(\d+)\.(\d+)\.(\d+)")]
    private static partial Regex SemverPrefix();

    [GeneratedRegex(@"^v(\d+)\.(\d+)\.(\d+)$")]
    private static partial Regex StableTag();

    [GeneratedRegex(@"^## \[(?<version>[^\]]+)\](?: - (?<date>\d{4}-\d{2}-\d{2}))?[ \t]*$", RegexOptions.Multiline)]
    private static partial Regex ChangelogHeading();

    [GeneratedRegex(@"Include=""Qyl\.Telemetry\.AutoInstrumentation[.\w]*""\s+Version=""([^""]+)""")]
    private static partial Regex ReadmeReference();

    [GeneratedRegex(@"\bpublic const int (V\d+)\s*=\s*(\d+)\s*;")]
    private static partial Regex AbiDeclaration();

    [GeneratedRegex(@"global::Qyl\.Telemetry\.AutoInstrumentation\.GeneratedCode\.QylGeneratedCodeAbi\.V\d+")]
    private static partial Regex AbiReference();

    [GeneratedRegex(@"\bVersion\s*=\s*""\d")]
    private static partial Regex VersionLiteral();
}
