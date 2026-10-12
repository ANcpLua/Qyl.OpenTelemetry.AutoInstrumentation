using System.Text;

namespace Qyl.Build;

/// <summary>What the consumer-behavior, smoke and evidence gates share: running a built consumer,
/// reading what the generator emitted into it, and comparing outputs line by line.</summary>
static class ConsumerFixture
{
    public const string GeneratedInterceptorFile = "QylAutoInstrumentation.Interceptors.g.cs";

    /// <summary>The project items that make a consumer write its generated sources to Generated/
    /// without compiling them twice.</summary>
    public const string EmitGeneratedProperties =
        "    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>\n" +
        "    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>\n";

    public const string RemoveGeneratedItem = "    <Compile Remove=\"Generated/**/*.cs\" />\n";

    /// <summary>A consumer project file: the given properties and items, and an optional build-assets
    /// import after the items (what a ProjectReference consumer needs in place of the package's own).</summary>
    public static void WriteProject(AbsolutePath path, string sdk, string properties, string items, string? import = null) =>
        File.WriteAllText(path,
            $"<Project Sdk=\"{sdk}\">\n" +
            "  <PropertyGroup>\n" +
            $"    <TargetFramework>{Repo.TargetFramework}</TargetFramework>\n" +
            "    <Nullable>enable</Nullable>\n" +
            "    <ImplicitUsings>enable</ImplicitUsings>\n" +
            properties +
            "  </PropertyGroup>\n" +
            "  <ItemGroup>\n" +
            items +
            "  </ItemGroup>\n" +
            (import is null ? string.Empty : $"  <Import Project=\"{import}\" />\n") +
            "</Project>\n");

    public static string PackageReference(string id, string version) =>
        $"    <PackageReference Include=\"{id}\" Version=\"{version}\" />\n";

    /// <summary>The managed assembly a Release build of a fixture project writes.</summary>
    public static AbsolutePath Assembly(AbsolutePath directory, string assemblyName) =>
        directory / "bin" / "Release" / Repo.TargetFramework / $"{assemblyName}.dll";

    public static ProcessResult RunAssembly(AbsolutePath assembly, IReadOnlyDictionary<string, string>? overrides = null, IEnumerable<string>? arguments = null) =>
        Shell.Run("dotnet", [assembly, .. arguments ?? []], assembly.Parent, Environment(overrides));

    public static ProcessResult RunExecutable(string executable, IReadOnlyDictionary<string, string>? overrides = null, IEnumerable<string>? arguments = null) =>
        Shell.Run(executable, arguments ?? [], Path.GetDirectoryName(executable), Environment(overrides));

    public static Dictionary<string, string> Environment(IReadOnlyDictionary<string, string>? overrides) =>
        overrides is null ? Shell.CleanEnvironment() : Shell.CleanEnvironment().With(overrides);

    /// <summary>Every generated interceptor source under the consumer's Generated/ folder.</summary>
    public static AbsolutePath[] GeneratedInterceptors(AbsolutePath directory)
    {
        var generated = directory / "Generated";
        return generated.DirectoryExists()
            ? generated.GlobFiles($"**/{GeneratedInterceptorFile}").OrderBy(static path => path.ToString(), StringComparer.Ordinal).ToArray()
            : [];
    }

    /// <summary>The one generated interceptor source of a consumer; fails unless exactly one exists.</summary>
    public static string SingleGeneratedInterceptor(AbsolutePath directory, string label)
    {
        var files = GeneratedInterceptors(directory);
        Gate.Require(files.Length == 1, $"{label}: expected exactly one generated interceptor source file, found {files.Length}");
        return File.ReadAllText(files[0]);
    }

    public static void RequireTokens(string label, string text, IEnumerable<string> tokens)
    {
        foreach (var token in tokens)
            Gate.Require(text.Contains(token, StringComparison.Ordinal), $"{label} missing token: {token}");
    }

    /// <summary>Exact output comparison; the failure names the first line that differs.</summary>
    public static void RequireOutput(string label, string actual, string expected)
    {
        if (string.Equals(actual, expected, StringComparison.Ordinal))
            return;
        throw Gate.Fail($"{label} mismatch\n{Diff(expected, actual)}\nEXPECTED\n{expected}\nACTUAL\n{actual}");
    }

    /// <summary>A short line diff: the lines only one side has, in order.</summary>
    public static string Diff(string expected, string actual)
    {
        var left = expected.Split('\n');
        var right = actual.Split('\n');
        var builder = new StringBuilder();
        var length = Math.Max(left.Length, right.Length);
        for (var index = 0; index < length; index++)
        {
            var a = index < left.Length ? left[index] : null;
            var b = index < right.Length ? right[index] : null;
            if (string.Equals(a, b, StringComparison.Ordinal))
                continue;
            if (a is not null)
                builder.AppendLine($"-{index + 1}: {a}");
            if (b is not null)
                builder.AppendLine($"+{index + 1}: {b}");
        }

        return builder.ToString();
    }
}
