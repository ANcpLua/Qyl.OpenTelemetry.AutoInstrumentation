namespace Qyl.AutoInstrumentation.Cli;

/// <summary>
/// qyl installer (M11). Replaces the manual cp-into-substrate-store + env juggling with
/// `qyl install`. Copies the bundled qyl plugin + semconv DLLs into every runtime folder of the
/// OpenTelemetry .NET substrate store ($OTEL_DOTNET_AUTO_HOME/net/&lt;tfm&gt;), then prints the
/// OTEL_DOTNET_AUTO_PLUGINS line to export. Never touches the substrate's own OpenTelemetry.dll.
/// </summary>
internal static class Program
{
    private const string PluginType =
        "Qyl.AutoInstrumentation.Plugin.Plugin, Qyl.AutoInstrumentation.Plugin";

    // Whitelist — only these are deployed, so the tool's own OpenTelemetry.dll never clobbers the substrate's.
    private static readonly string[] Dlls =
    {
        "Qyl.AutoInstrumentation.Plugin.dll",
        "Qyl.OpenTelemetry.SemanticConventions.dll",
        "Qyl.OpenTelemetry.SemanticConventions.Incubating.dll",
    };

    private static int Main(string[] args)
    {
        var cmd = args.Length > 0 ? args[0] : "help";
        return cmd switch
        {
            "install" => Deploy(install: true),
            "uninstall" => Deploy(install: false),
            _ => Help(),
        };
    }

    private static string Home() =>
        Environment.GetEnvironmentVariable("OTEL_DOTNET_AUTO_HOME")
        ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".otel-dotnet-auto");

    private static int Deploy(bool install)
    {
        var netDir = Path.Combine(Home(), "net");
        if (!Directory.Exists(netDir))
        {
            Console.Error.WriteLine($"qyl: substrate not found at {Home()} — install opentelemetry-dotnet-instrumentation first.");
            return 1;
        }

        var toolDir = AppContext.BaseDirectory;
        var tfms = Directory.GetDirectories(netDir, "net*");
        var count = 0;

        foreach (var tfm in tfms)
        {
            foreach (var dll in Dlls)
            {
                var dst = Path.Combine(tfm, dll);
                if (install)
                {
                    var src = Path.Combine(toolDir, dll);
                    if (!File.Exists(src))
                    {
                        Console.Error.WriteLine($"qyl: bundled '{dll}' missing from the tool package.");
                        return 1;
                    }
                    File.Copy(src, dst, overwrite: true);
                    count++;
                }
                else if (File.Exists(dst))
                {
                    File.Delete(dst);
                    count++;
                }
            }
        }

        Console.WriteLine($"qyl: {(install ? "installed" : "removed")} {count} file(s) across {tfms.Length} runtime(s) under {netDir}");
        if (install)
        {
            Console.WriteLine();
            Console.WriteLine("Enable the plugin (after sourcing the substrate's instrument.sh):");
            Console.WriteLine($"  export OTEL_DOTNET_AUTO_PLUGINS=\"{PluginType}\"");
        }
        return 0;
    }

    private static int Help()
    {
        Console.WriteLine("qyl — auto-instrumentation installer");
        Console.WriteLine("  qyl install     deploy the qyl plugin into the OpenTelemetry .NET substrate store");
        Console.WriteLine("  qyl uninstall   remove it");
        return 0;
    }
}
