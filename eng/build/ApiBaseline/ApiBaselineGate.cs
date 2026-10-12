namespace Qyl.Build;

/// <summary>
/// The public API surface of every shipped project is baselined by the PublicApiAnalyzers, and the
/// runtime and generator projects fail the build on an undocumented public member. The Build target
/// compiles both under those rules; this gate holds the configuration that makes the build enforce them.
/// </summary>
static class ApiBaselineGate
{
    private static readonly string[] PublicApiProjects =
    [
        "Qyl.Telemetry.AutoInstrumentation",
        "Qyl.Telemetry.AutoInstrumentation.DiagnosticListeners",
        "Qyl.Telemetry.AutoInstrumentation.Hosting",
        "Qyl.Telemetry.AutoInstrumentation.EntityFrameworkCore",
        "Qyl.Telemetry.AutoInstrumentation.SqlClient",
        "Qyl.Telemetry.Hosting",
    ];

    private static readonly string[] ExcludedProjects =
    [
        "Qyl.Telemetry.AutoInstrumentation.SourceGenerators",
        "Qyl.RealAspNetCoreDemo",
        "Qyl.RealEfCoreDemo",
        "Qyl.RealGrpcClientDemo",
        "Qyl.RealHttpClientDemo",
        "Qyl.RealSessionPropagationDemo",
        "Qyl.RealSqlClientDemo",
    ];

    private static readonly string[] XmlDocTokens =
    [
        "<GenerateDocumentationFile>true</GenerateDocumentationFile>",
        "<WarningsAsErrors>$(WarningsAsErrors);CS1591</WarningsAsErrors>",
    ];

    public static void Verify()
    {
        VerifyProps();
        foreach (var project in PublicApiProjects)
        {
            VerifyApiFile(Repo.Src / project / "PublicAPI.Shipped.txt", requireEntries: true);
            VerifyApiFile(Repo.Src / project / "PublicAPI.Unshipped.txt", requireEntries: false);
        }

        Log.Information("public-api-baseline-ok");

        VerifyXmlDocContract(Repo.GeneratorProject, "source generator");
        VerifyXmlDocContract(Repo.CoreProject, "runtime");
        Log.Information("xml-doc-enforcement-ok scope=source-generator,runtime");
    }

    private static void VerifyProps()
    {
        var props = File.ReadAllText(Repo.BuildProps);
        var packages = File.ReadAllText(Repo.PackagesProps);
        foreach (var token in new[] { "Microsoft.CodeAnalysis.PublicApiAnalyzers", "QylEnablePublicApiAnalyzers" })
            Gate.Require(props.Contains(token, StringComparison.Ordinal), $"Directory.Build.props missing PublicAPI token: {token}");

        foreach (var token in new[] { "<PackageVersion Include=\"Microsoft.CodeAnalysis.PublicApiAnalyzers\" Version=\"5.6.0\" />", "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>" })
            Gate.Require(packages.Contains(token, StringComparison.Ordinal), $"Directory.Packages.props missing PublicAPI token: {token}");

        foreach (var project in ExcludedProjects)
        {
            Gate.Require(!props.Contains(project, StringComparison.Ordinal) || project == "Qyl.Telemetry.AutoInstrumentation.SourceGenerators",
                $"Directory.Build.props should not explicitly enable PublicAPI analyzers for {project}");
        }
    }

    private static void VerifyApiFile(AbsolutePath path, bool requireEntries)
    {
        Gate.Require(path.FileExists(), $"missing PublicAPI file: {path}");
        var lines = File.ReadAllText(path).Split('\n').Select(static line => line.TrimEnd('\r')).ToList();
        if (lines.Count > 0 && lines[^1].Length == 0)
            lines.RemoveAt(lines.Count - 1);
        Gate.Require(lines.Count > 0 && lines[0] == "#nullable enable", $"{path} must start with #nullable enable");
        Gate.Require(!requireEntries || lines.Skip(1).Any(static line => line.Trim().Length > 0), $"{path} must contain shipped public API entries");
    }

    private static void VerifyXmlDocContract(AbsolutePath project, string label)
    {
        var text = File.ReadAllText(project);
        foreach (var token in XmlDocTokens)
            Gate.Require(text.Contains(token, StringComparison.Ordinal), $"{label} XML-doc enforcement token missing: {token}");
    }
}
