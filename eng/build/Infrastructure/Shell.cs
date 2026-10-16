using System.Collections;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;

namespace Qyl.Build;

/// <summary>What a finished child process left behind. Stdout and stderr are kept apart and byte-exact:
/// several gates compare them verbatim or require stderr to be empty.</summary>
sealed record ProcessResult(string Command, int ExitCode, string Stdout, string Stderr, int ProcessId, TimeSpan Elapsed)
{
    public string Describe() => $"{Command}\nexit={ExitCode}\nstdout={Stdout}\nstderr={Stderr}";
}

/// <summary>
/// Runs child processes with an environment this build decides completely. No OTEL_* or QYL_*
/// variable of the caller's shell reaches a child: ambient configuration must not change what a gate
/// verifies, so a child sees exactly the overrides a gate names.
/// </summary>
static class Shell
{
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromMinutes(30);

    public static int FreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        return ((IPEndPoint)listener.LocalEndpoint).Port;
    }

    public static Dictionary<string, string> CleanEnvironment()
    {
        var environment = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (DictionaryEntry entry in Environment.GetEnvironmentVariables())
        {
            var key = (string)entry.Key;
            if (key.StartsWith("OTEL_", StringComparison.Ordinal) || key.StartsWith("QYL_", StringComparison.Ordinal))
                continue;
            environment[key] = (string?)entry.Value ?? string.Empty;
        }

        environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        environment["DOTNET_NOLOGO"] = "1";
        environment["MSBUILDDISABLENODEREUSE"] = "1";
        environment["DOTNET_PROCESSOR_COUNT"] = "4";
        return environment;
    }

    public static Dictionary<string, string> With(this IReadOnlyDictionary<string, string> environment, IReadOnlyDictionary<string, string> overrides)
    {
        var merged = new Dictionary<string, string>(environment, StringComparer.Ordinal);
        foreach (var (key, value) in overrides)
            merged[key] = value;
        return merged;
    }

    public static ProcessResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        bool echo = false)
    {
        using var process = RunningProcess.Start(fileName, arguments, workingDirectory, environment, echo);
        return process.WaitForExit(timeout ?? DefaultTimeout);
    }

    /// <summary>Runs a command and fails the gate on a non-zero exit, with the whole output.</summary>
    public static ProcessResult Checked(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        TimeSpan? timeout = null,
        bool echo = false)
    {
        var result = Run(fileName, arguments, workingDirectory, environment, timeout, echo);
        Gate.Require(result.ExitCode == 0, () => "command failed: " + result.Describe());
        return result;
    }

    public static string Quote(IEnumerable<string> arguments) =>
        string.Join(' ', arguments.Select(static argument => argument.Length == 0 || argument.Any(char.IsWhiteSpace) ? $"\"{argument}\"" : argument));
}

/// <summary>A child process whose output is captured as it runs, for the long-lived ones (the
/// collector, weaver, a downstream server) that a gate starts, talks to and stops.</summary>
sealed class RunningProcess : IDisposable
{
    private readonly Process _process;
    private readonly StringBuilder _stdout = new();
    private readonly StringBuilder _stderr = new();
    private readonly Task _stdoutPump;
    private readonly Task _stderrPump;
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();

    private RunningProcess(Process process, string command, bool echo)
    {
        _process = process;
        Command = command;
        var prefix = Path.GetFileName(process.StartInfo.FileName);
        _stdoutPump = Pump(process.StandardOutput, _stdout, echo ? prefix : null);
        _stderrPump = Pump(process.StandardError, _stderr, echo ? prefix : null);
    }

    public string Command { get; }

    public int ProcessId => _process.Id;

    public bool HasExited => _process.HasExited;

    public string Stdout { get { lock (_stdout) return _stdout.ToString(); } }

    public string Stderr { get { lock (_stderr) return _stderr.ToString(); } }

    public static RunningProcess Start(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        IReadOnlyDictionary<string, string>? environment = null,
        bool echo = false)
    {
        var startInfo = new ProcessStartInfo(fileName)
        {
            WorkingDirectory = workingDirectory ?? Repo.Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = false,
            UseShellExecute = false,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var argument in arguments)
            startInfo.ArgumentList.Add(argument);

        startInfo.Environment.Clear();
        foreach (var (key, value) in environment ?? Shell.CleanEnvironment())
            startInfo.Environment[key] = value;

        var command = fileName + " " + Shell.Quote(startInfo.ArgumentList);
        Log.Debug("> {Command}", command);
        var process = Process.Start(startInfo) ?? throw Gate.Fail($"could not start {command}");
        return new RunningProcess(process, command, echo);
    }

    public ProcessResult WaitForExit(TimeSpan timeout)
    {
        if (!_process.WaitForExit(timeout))
        {
            Kill();
            throw Gate.Fail($"{Command} did not finish within {timeout.TotalSeconds:0}s\nstdout={Stdout}\nstderr={Stderr}");
        }

        _process.WaitForExit();
        Task.WaitAll(_stdoutPump, _stderrPump);
        return new ProcessResult(Command, _process.ExitCode, Stdout, Stderr, _process.Id, _stopwatch.Elapsed);
    }

    public async Task<ProcessResult> WaitForExitAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = new CancellationTokenSource(timeout);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, deadline.Token);
        try
        {
            await _process.WaitForExitAsync(linked.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            Kill();
            await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            throw Gate.Fail($"{Command} did not finish within {timeout.TotalSeconds:0}s\nstdout={Stdout}\nstderr={Stderr}");
        }

        await Task.WhenAll(_stdoutPump, _stderrPump).ConfigureAwait(false);
        return new ProcessResult(Command, _process.ExitCode, Stdout, Stderr, _process.Id, _stopwatch.Elapsed);
    }

    public void Kill()
    {
        try
        {
            if (!_process.HasExited)
                _process.Kill(entireProcessTree: true);
            _process.WaitForExit(10_000);
        }
        catch (InvalidOperationException)
        {
            // Already gone.
        }
    }

    public void Dispose()
    {
        Kill();
        _process.Dispose();
    }

    private static async Task Pump(StreamReader reader, StringBuilder sink, string? echoPrefix)
    {
        var buffer = new char[4096];
        var line = new StringBuilder();
        int read;
        while ((read = await reader.ReadAsync(buffer).ConfigureAwait(false)) > 0)
        {
            lock (sink)
                sink.Append(buffer, 0, read);

            if (echoPrefix is null)
                continue;

            for (var index = 0; index < read; index++)
            {
                if (buffer[index] == '\n')
                {
                    Log.Information("[{Prefix}] {Line}", echoPrefix, line.ToString().TrimEnd('\r'));
                    line.Clear();
                }
                else
                {
                    line.Append(buffer[index]);
                }
            }
        }

        if (echoPrefix is not null && line.Length > 0)
            Log.Information("[{Prefix}] {Line}", echoPrefix, line.ToString());
    }
}

/// <summary>The dotnet CLI as every gate calls it: Release, at most four MSBuild nodes, no node reuse.</summary>
static class DotNet
{
    public static ProcessResult Build(string project, IEnumerable<string>? extra = null, IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null) =>
        Shell.Checked("dotnet", ["build", project, "-c", "Release", "-maxcpucount:4", "-nodeReuse:false", "-v", "quiet", "-nologo", .. extra ?? []], workingDirectory, environment);

    public static ProcessResult Publish(string project, IEnumerable<string>? extra = null, IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null) =>
        Shell.Checked("dotnet", ["publish", project, "-c", "Release", "-maxcpucount:4", "-nodeReuse:false", "-v", "quiet", "-nologo", .. extra ?? []], workingDirectory, environment);

    public static ProcessResult Pack(string project, string output, IEnumerable<string>? extra = null, IReadOnlyDictionary<string, string>? environment = null) =>
        Shell.Checked("dotnet", ["pack", project, "-c", "Release", "-o", output, "-maxcpucount:4", "-nodeReuse:false", "-v", "quiet", "-nologo", .. extra ?? []], null, environment);

    /// <summary>A NativeAOT publish for the host runtime into <paramref name="output"/>; returns the executable.</summary>
    public static string PublishNativeAot(string project, string output, IEnumerable<string>? extra = null, IReadOnlyDictionary<string, string>? environment = null, string? workingDirectory = null)
    {
        Publish(project, ["-r", Repo.RuntimeIdentifier, "-p:PublishAot=true", "--self-contained", "true", "-o", output, .. extra ?? []], environment, workingDirectory);
        var executable = Path.Combine(output, Repo.ExecutableName(Path.GetFileNameWithoutExtension(project)));
        Gate.Require(File.Exists(executable), $"NativeAOT executable missing: {executable}");
        return executable;
    }
}
