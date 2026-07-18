#!/usr/bin/env python3
from __future__ import annotations

import platform
import subprocess
import tempfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
CORE_PROJECT = ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(activityListener);

await using var server = LoopbackHttpServer.Start(HttpStatusCode.InternalServerError, HttpStatusCode.NoContent);
using var http = new HttpClient { BaseAddress = server.Uri };
http.DefaultRequestHeaders.Add("x-qyl-source", "default-header");

try
{
    await http.GetStringAsync("failure");
}
catch (HttpRequestException exception)
{
    Console.WriteLine("http.exception.status=" + ((int?)exception.StatusCode)?.ToString(System.Globalization.CultureInfo.InvariantCulture));
}

using var content = new StringContent("payload");
content.Headers.Add("x-qyl-content", "content-header");
using (await http.PostAsync("content", content))
{
}
await server.RequestCompleted;

var failure = captured.Single(activity => HasTag(activity, "http.response.status_code", "500"));
var failureTags = Tags(failure);
Console.WriteLine("http.activity.status=" + failure.Status);
Console.WriteLine("server.address" + "=" + failureTags["server.address"]);
Console.WriteLine("http.request.header.x-qyl-source=" + failureTags["http.request.header.x-qyl-source"]);
Console.WriteLine("http.response.status_code" + "=" + failureTags["http.response.status_code"]);
Console.WriteLine("error.type" + "=" + failureTags["error.type"]);

var success = captured.Single(activity => HasTag(activity, "http.response.status_code", "204"));
var successTags = Tags(success);
Console.WriteLine("http.request.header.x-qyl-content=" + successTags["http.request.header.x-qyl-content"]);
Console.WriteLine("activity.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

return captured.Count == 2 ? 0 : 1;

static bool HasTag(Activity activity, string key, string expected)
    => activity.TagObjects.Any(tag => tag.Key == key &&
        string.Equals(Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture), expected, StringComparison.Ordinal));

static Dictionary<string, string> Tags(Activity activity)
    => activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => tag.Value is string[] values
            ? string.Join(",", values)
            : Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);

internal sealed class LoopbackHttpServer : IAsyncDisposable
{
    private readonly TcpListener _listener;

    private LoopbackHttpServer(TcpListener listener, HttpStatusCode[] statuses)
    {
        _listener = listener;
        Uri = new Uri($"http://127.0.0.1:{((IPEndPoint)listener.LocalEndpoint).Port}/", UriKind.Absolute);
        RequestCompleted = ServeAsync(listener, statuses);
    }

    public Uri Uri { get; }

    public Task RequestCompleted { get; }

    public static LoopbackHttpServer Start(params HttpStatusCode[] statuses)
    {
        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start(statuses.Length);
        return new LoopbackHttpServer(listener, statuses);
    }

    public ValueTask DisposeAsync()
    {
        _listener.Stop();
        return ValueTask.CompletedTask;
    }

    private static async Task ServeAsync(TcpListener listener, HttpStatusCode[] statuses)
    {
        foreach (var status in statuses)
        {
            using var client = await listener.AcceptTcpClientAsync();
            await using var stream = client.GetStream();
            await ReadRequestAsync(stream);
            var reason = status == HttpStatusCode.NoContent ? "No Content" : "Internal Server Error";
            var response = Encoding.ASCII.GetBytes(
                $"HTTP/1.1 {(int)status} {reason}\r\nContent-Length: 0\r\nConnection: close\r\n\r\n");
            await stream.WriteAsync(response);
        }
    }

    private static async Task ReadRequestAsync(NetworkStream stream)
    {
        var buffer = new byte[4096];
        var received = new List<byte>();
        while (true)
        {
            var count = await stream.ReadAsync(buffer);
            if (count == 0)
                throw new InvalidOperationException("Loopback client closed before sending HTTP headers.");
            received.AddRange(buffer.AsSpan(0, count).ToArray());
            if (received.Count >= 4 && received.ToArray().AsSpan().IndexOf("\r\n\r\n"u8) >= 0)
                return;
        }
    }
}
'''


EXPECTED = """http.exception.status=500
http.activity.status=Error
server.address=127.0.0.1
http.request.header.x-qyl-source=default-header
http.response.status_code=500
error.type=500
http.request.header.x-qyl-content=content-header
activity.count=2
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            run_checked(["dotnet", "pack", str(CORE_PROJECT), "-c", "Release", "-o", str(feed), "-v", "quiet"], ROOT, env)
        finally:
            if fcntl is not None:
                fcntl.flock(lock, fcntl.LOCK_UN)


def write_project(directory: Path, feed: Path, packages: Path, version: str) -> Path:
    directory.mkdir(parents=True)
    project = directory / "Consumer.csproj"
    project.write_text(
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
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"
    fail(f"unsupported NativeAOT platform: {platform.system()} {platform.machine()}")


def verify_generated_source(directory: Path) -> None:
    generated = sorted((directory / "Generated").rglob("QylAutoInstrumentation.Interceptors.g.cs"))
    if len(generated) != 1:
        fail(f"expected one generated interceptor source, found {len(generated)}")
    text = generated[0].read_text(encoding="utf-8")
    for token in [
        "namespace Qyl.OpenTelemetry.AutoInstrumentation.Generated",
        "HttpClient_GetStringAsync_",
        "QylInterceptedHttpClient.GetStringAsync(",
        "HttpClient_PostAsync_",
        "QylInterceptedHttpClient.PostAsync(",
    ]:
        if token not in text:
            fail(f"generated interceptor source missing token: {token}")


def run_consumer(project: Path, env: dict[str, str], *, nativeaot: bool) -> subprocess.CompletedProcess[str]:
    if nativeaot:
        output = project.parent / "publish"
        run_checked(
            [
                "dotnet", "publish", str(project), "-c", "Release", "-r", runtime_identifier(),
                "-p:PublishAot=true", "--self-contained", "true", "-o", str(output), "-v", "quiet",
            ],
            project.parent,
            env,
        )
        executable = output / ("Consumer.exe" if platform.system().lower() == "windows" else "Consumer")
        return subprocess.run([str(executable)], cwd=output, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)

    run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
    verify_generated_source(project.parent)
    assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"
    return subprocess.run(["dotnet", str(assembly)], cwd=project.parent, env=env, text=True, stdout=subprocess.PIPE, stderr=subprocess.PIPE)


def verify_completed(name: str, completed: subprocess.CompletedProcess[str]) -> None:
    if completed.returncode != 0 or completed.stderr or completed.stdout != EXPECTED:
        fail(
            f"{name} failed\nexit={completed.returncode}\n"
            f"EXPECTED\n{EXPECTED}\nACTUAL\n{completed.stdout}\nSTDERR\n{completed.stderr}"
        )


def main() -> None:
    env = clean_env()
    env["OTEL_DOTNET_AUTO_TRACES_HTTP_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"] = "x-qyl-source,x-qyl-content"
    with tempfile.TemporaryDirectory(prefix="qyl-source-interceptor-") as temp:
        root = Path(temp)
        feed = root / "feed"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, root / "packages", read_version())
        managed = run_consumer(project, env, nativeaot=False)
        nativeaot = run_consumer(project, env, nativeaot=True)
    verify_completed("managed consumer", managed)
    verify_completed("NativeAOT consumer", nativeaot)
    print("source-interceptor-consumer-ok")


if __name__ == "__main__":
    main()
