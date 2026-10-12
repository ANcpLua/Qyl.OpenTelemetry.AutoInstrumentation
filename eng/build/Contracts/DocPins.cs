using System.Text.RegularExpressions;

namespace Qyl.Build;

/// <summary>
/// Fails when a README or contract YAML names a version that Directory.Packages.props does not pin.
/// Directory.Packages.props is the one source of package versions; the READMEs and the hand-written
/// contract YAMLs repeat those versions for readers, and every copy drifted once already. Compared:
/// <list type="bullet">
/// <item>a package id followed by a version, and each "Pinned" table row, against Directory.Packages.props;</item>
/// <item>a backticked NuGet range, against the MassTransit.RabbitMQ range;</item>
/// <item>the live-check registry tag, against SEMCONV_REF in tools/live-check.env;</item>
/// <item>the N-row contract count, against the total in contracts/coverage-matrix.md;</item>
/// <item>a QylGeneratedCodeAbi.V&lt;N&gt; anchor, against the Directory.Build.props major;</item>
/// <item>a nuget.org/packages/&lt;id&gt;/&lt;version&gt; link in a contract YAML, against Directory.Packages.props.</item>
/// </list>
/// The "verified against" strings in the contract YAMLs are derived from the same pins by ContractModel.
/// </summary>
static partial class DocPins
{
    private const string VersionPattern = @"\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?";

    // Table rows whose first cell names the library rather than the package id.
    private static readonly Dictionary<string, string[]> RowPackages = new(StringComparer.Ordinal)
    {
        ["MySql.Data"] = ["MySql.Data"],
        ["Elastic.Transport, Elastic.Clients.Elasticsearch"] = ["Elastic.Transport", "Elastic.Clients.Elasticsearch"],
        ["GraphQL.NET"] = ["GraphQL"],
        ["Grpc.Net.Client"] = ["Grpc.Net.Client"],
        ["MassTransit"] = ["MassTransit.RabbitMQ"],
        ["MongoDB.Driver"] = ["MongoDB.Driver"],
        ["MySqlConnector"] = ["MySqlConnector"],
        ["Npgsql"] = ["Npgsql"],
        ["NServiceBus"] = ["NServiceBus"],
        ["Oracle.ManagedDataAccess.Core"] = ["Oracle.ManagedDataAccess.Core"],
        ["Quartz.NET"] = ["Quartz"],
        ["RabbitMQ.Client"] = ["RabbitMQ.Client"],
        ["Confluent.Kafka"] = ["Confluent.Kafka"],
        ["StackExchange.Redis"] = ["StackExchange.Redis"],
    };

    private static AbsolutePath[] ContractYamls => [Repo.ContractsDirectory / "qyl-native-instrumentations.yaml", Repo.ContractsDirectory / "qyl-aot-ownership.yaml"];

    public static void Verify()
    {
        var pins = Repo.PackagePins;
        var errors = new List<string>();
        AbsolutePath[] readmes = [Repo.Root / "README.md", .. Repo.Src.GlobFiles("*/README.md").OrderBy(static path => path.ToString(), StringComparer.Ordinal)];
        foreach (var path in readmes)
        {
            var text = File.ReadAllText(path);
            CheckPackageMentions(path, text, pins, errors);
            CheckPinnedRows(path, text, pins, errors);
            CheckRanges(path, text, pins, errors);
            CheckRegistryTag(path, text, errors);
            CheckRowCount(path, text, errors);
            CheckAbiAnchor(path, text, errors);
        }

        foreach (var path in ContractYamls)
            CheckContractLinks(path, File.ReadAllText(path), pins, errors);

        Gate.Require(errors.Count == 0, () => string.Join("\n", errors) + $"\ndoc-pins: {errors.Count} copied version(s) differ from their source");
        Log.Information("doc-pins-ok");
    }

    private static int LineOf(string text, int index) => text.AsSpan(0, index).Count('\n') + 1;

    private static void CheckPackageMentions(AbsolutePath path, string text, IReadOnlyDictionary<string, string> pins, List<string> errors)
    {
        foreach (var (packageId, version) in pins)
        {
            var pattern = new Regex(@"(?<![\w.])`?" + Regex.Escape(packageId) + @"(?![\w.])`?\s+`?(" + VersionPattern + ")`?");
            foreach (Match match in pattern.Matches(text))
            {
                if (match.Groups[1].Value != version)
                    errors.Add($"{Repo.Relative(path)}:{LineOf(text, match.Index)}: {packageId} {match.Groups[1].Value} != pin {version}");
            }
        }
    }

    private static void CheckPinnedRows(AbsolutePath path, string text, IReadOnlyDictionary<string, string> pins, List<string> errors)
    {
        var inPinnedTable = false;
        var number = 0;
        foreach (var line in text.Split('\n'))
        {
            number++;
            if (!line.StartsWith("| ", StringComparison.Ordinal))
            {
                inPinnedTable = false;
                continue;
            }

            var cells = line.Trim().Trim('|').Split('|').Select(static cell => cell.Trim()).ToArray();
            if (cells.Length >= 2 && cells[1] == "Pinned")
            {
                inPinnedTable = true;
                continue;
            }

            if (!inPinnedTable || !RowPackages.TryGetValue(cells[0], out var packages))
                continue;
            var cited = BacktickValue().Matches(cells[1]).Select(static match => match.Groups[1].Value).ToArray();
            var expected = packages.Select(packageId => pins[packageId]).ToArray();
            if (!cited.SequenceEqual(expected))
                errors.Add($"{Repo.Relative(path)}:{number}: {cells[0]} pinned {ContractValue.Render(cited.ToList())} != {ContractValue.Render(expected.ToList())}");
        }
    }

    private static void CheckRanges(AbsolutePath path, string text, IReadOnlyDictionary<string, string> pins, List<string> errors)
    {
        var expected = pins["MassTransit.RabbitMQ"];
        foreach (Match match in NuGetRange().Matches(text))
        {
            if (match.Groups[1].Value != expected)
                errors.Add($"{Repo.Relative(path)}:{LineOf(text, match.Index)}: range {match.Groups[1].Value} != MassTransit.RabbitMQ pin {expected}");
        }
    }

    private static void CheckRegistryTag(AbsolutePath path, string text, List<string> errors)
    {
        var reference = SemconvRef().Match(File.ReadAllText(Repo.LiveCheckEnv));
        Gate.Require(reference.Success, $"doc-pins: {Repo.Relative(Repo.LiveCheckEnv)} sets no SEMCONV_REF");
        foreach (Match match in new Regex("`(v" + VersionPattern + ")` registry").Matches(text))
        {
            if (match.Groups[1].Value != reference.Groups[1].Value)
                errors.Add($"{Repo.Relative(path)}: registry {match.Groups[1].Value} != SEMCONV_REF {reference.Groups[1].Value}");
        }
    }

    private static void CheckRowCount(AbsolutePath path, string text, List<string> errors)
    {
        var total = TotalRow().Match(File.ReadAllText(ContractModel.CoverageMatrixPath));
        Gate.Require(total.Success, $"doc-pins: {Repo.Relative(ContractModel.CoverageMatrixPath)} has no 'Total contract items' row");
        foreach (Match match in RowContract().Matches(text))
        {
            if (match.Groups[1].Value != total.Groups[1].Value)
                errors.Add($"{Repo.Relative(path)}: {match.Groups[1].Value}-row contract != coverage matrix total {total.Groups[1].Value}");
        }
    }

    private static void CheckAbiAnchor(AbsolutePath path, string text, List<string> errors)
    {
        var version = MajorVersion().Match(File.ReadAllText(Repo.BuildProps));
        Gate.Require(version.Success, $"doc-pins: {Repo.Relative(Repo.BuildProps)} has no <Version>");
        foreach (Match match in AbiAnchor().Matches(text))
        {
            if (match.Groups[1].Value != version.Groups[1].Value)
                errors.Add($"{Repo.Relative(path)}: QylGeneratedCodeAbi.V{match.Groups[1].Value} != major {version.Groups[1].Value}");
        }
    }

    private static void CheckContractLinks(AbsolutePath path, string text, IReadOnlyDictionary<string, string> pins, List<string> errors)
    {
        var lowered = pins.ToDictionary(static pair => pair.Key.ToLowerInvariant(), static pair => pair, StringComparer.Ordinal);
        foreach (Match match in new Regex(@"nuget\.org/packages/([\w.]+)/(" + VersionPattern + ")").Matches(text))
        {
            if (lowered.TryGetValue(match.Groups[1].Value.ToLowerInvariant(), out var entry) && match.Groups[2].Value != entry.Value)
                errors.Add($"{Repo.Relative(path)}:{LineOf(text, match.Index)}: {entry.Key} link {match.Groups[2].Value} != pin {entry.Value}");
        }

        CheckPackageMentions(path, text, pins, errors);
    }

    [GeneratedRegex("`([^`]+)`")]
    private static partial Regex BacktickValue();

    [GeneratedRegex(@"`(\[\d[^`\],]*,[^`\]]*[\])])`")]
    private static partial Regex NuGetRange();

    [GeneratedRegex(@"^\s*SEMCONV_REF\s*=\s*(\S+)\s*$", RegexOptions.Multiline)]
    private static partial Regex SemconvRef();

    [GeneratedRegex(@"^\| Total contract items \| (\d+) \|$", RegexOptions.Multiline)]
    private static partial Regex TotalRow();

    [GeneratedRegex(@"(\d+)-row contract")]
    private static partial Regex RowContract();

    [GeneratedRegex(@"<Version>(\d+)\.")]
    private static partial Regex MajorVersion();

    [GeneratedRegex(@"QylGeneratedCodeAbi\.V(\d+)")]
    private static partial Regex AbiAnchor();
}
