#!/usr/bin/env python3
"""Negative-space gate: a DISABLED instrumentation actually emits zero telemetry.

The interceptor is compiled in unconditionally; disabling is a runtime gate inside the
forwarding helper (QylInterceptedHttpClient.cs: `if (!observation.IsEnabled) return
SendOriginal(...)`). Every other verifier proves the enabled path or that options are *read*;
none fire a real request with instrumentation OFF and assert no span escapes. This does.

Control (enabled)  -> exactly one HttpClient span.
Disabled (http)    -> zero spans, via OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED=false.
Disabled (global)  -> zero spans, via OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED=false.
"""
from __future__ import annotations

import subprocess
import tempfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked

ROOT = Path(__file__).resolve().parents[1]
CORE_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"

PROGRAM = r'''
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(listener);

// qyl publishes http.client.request.duration on a Meter named System.Net.Http, the
// same identity the BCL uses. qyl measurements carry ONLY http.request.method (+
// status code); BCL measurements always carry server.address. Count qyl's only.
var qylMeasurements = 0;
using var meterListener = new MeterListener();
meterListener.InstrumentPublished = static (instrument, l) =>
{
    if (instrument.Meter.Name == "System.Net.Http" && instrument.Name == "http.client.request.duration")
        l.EnableMeasurementEvents(instrument);
};
meterListener.SetMeasurementEventCallback<double>((instrument, value, tags, state) =>
{
    var hasMethod = false;
    var bclShaped = false;
    foreach (var tag in tags)
    {
        if (tag.Key == "http.request.method")
            hasMethod = true;
        else if (tag.Key == "server.address")
            bclShaped = true;
    }

    if (hasMethod && !bclShaped)
        Interlocked.Increment(ref qylMeasurements);
});
meterListener.Start();

await using var server = LoopbackHttpServer.Start();
using var http = new HttpClient();
using (await http.GetAsync(server.Uri + "probe"))
{
}
await server.RequestCompleted;

var capturingLogger = new CapturingLogger();
ILogger logger = capturingLogger;
logger.Log(
    LogLevel.Warning,
    new EventId(7, "disabled-behavior"),
    "probe-log",
    exception: null,
    static (state, exception) => state);

var httpClientSpans = captured.Count(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == "qyl.instrumentation.domain" &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            "http.client",
            StringComparison.Ordinal)));

var iloggerSpans = captured.Count(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == "qyl.instrumentation.domain" &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            "log.ilogger",
            StringComparison.Ordinal)));

Console.WriteLine("httpclient.spans=" + httpClientSpans.ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("ilogger.spans=" + iloggerSpans.ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("httpclient.measurements=" + qylMeasurements.ToString(System.Globalization.CultureInfo.InvariantCulture));
return 0;

internal sealed class CapturingLogger : ILogger
{
    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
    }
}

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private LoopbackHttpServer(TcpListener listener)
    {
        _listener = listener;
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", UriKind.Absolute);
        RequestCompleted = ServeOnceAsync(listener);
    }

    public Uri Uri { get; }

    public Task RequestCompleted { get; }

    public static LoopbackHttpServer Start()
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(1);
        return new LoopbackHttpServer(listener);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task ServeOnceAsync(TcpListener listener)
    {
        using var client = await listener.AcceptTcpClientAsync();
        await using var stream = client.GetStream();
        var buffer = new byte[4096];
        var received = new List<byte>();
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                throw new InvalidOperationException("Loopback client closed before sending HTTP headers.");
            received.AddRange(buffer.AsSpan(0, count).ToArray());
            if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
                break;
        }

        var response = Encoding.ASCII.GetBytes("HTTP/1.1 204 No Content\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
        await stream.WriteAsync(response);
    }
}
'''


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    for project in [CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT]:
        run_checked(
            ["dotnet", "pack", str(project), "-c", "Release", "-o", str(feed), "-v", "quiet"],
            ROOT,
            env,
        )


def write_project(directory: Path, feed: Path, packages: Path, version: str) -> Path:
    directory.mkdir(parents=True)
    project_path = directory / "Consumer.csproj"
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{feed};{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation" Version="{version}" />
    <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def run_scenario(assembly: Path, base_env: dict[str, str], overrides: dict[str, str]) -> str:
    env = dict(base_env)
    env.update(overrides)
    completed = subprocess.run(
        ["dotnet", str(assembly)],
        cwd=assembly.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(f"consumer run failed\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}")
    if completed.stderr:
        fail(f"consumer wrote stderr:\n{completed.stderr}")
    return completed.stdout.strip()


def assert_spans(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        fail(f"{name}: expected '{expected}', got '{actual}'")
    print(f"  {name}: {actual}")


def main() -> None:
    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-disabled-behavior-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
        assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"

        def expect(spans: int, logs: int, measurements: int) -> str:
            return f"httpclient.spans={spans}\nilogger.spans={logs}\nhttpclient.measurements={measurements}"

        # Control: everything on -> one span per lane, one qyl measurement.
        assert_spans("enabled (control)", run_scenario(assembly, env, {}), expect(1, 1, 1))
        # Per-integration trace kill switch: only the HttpClient TRACE lane dies.
        assert_spans(
            "http trace instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED": "false"}),
            expect(0, 1, 1),
        )
        # Global kill switch: every lane dies.
        assert_spans(
            "global instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED": "false"}),
            expect(0, 0, 0),
        )
        # Signal-level switches: exactly one signal dies each time.
        assert_spans(
            "traces signal disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_INSTRUMENTATION_ENABLED": "false"}),
            expect(0, 1, 1),
        )
        assert_spans(
            "metrics signal disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_METRICS_INSTRUMENTATION_ENABLED": "false"}),
            expect(1, 1, 0),
        )
        assert_spans(
            "logs signal disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_LOGS_INSTRUMENTATION_ENABLED": "false"}),
            expect(1, 0, 1),
        )
        # Per-integration switches on the metrics and logs signals.
        assert_spans(
            "http metrics instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_METRICS_HTTPCLIENT_INSTRUMENTATION_ENABLED": "false"}),
            expect(1, 1, 0),
        )
        assert_spans(
            "ilogger logs instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_LOGS_ILOGGER_INSTRUMENTATION_ENABLED": "false"}),
            expect(1, 0, 1),
        )

    print("instrumentation-disabled-behavior-ok")


if __name__ == "__main__":
    main()
