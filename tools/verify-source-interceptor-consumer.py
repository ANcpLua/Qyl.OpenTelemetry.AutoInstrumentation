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
CORE_PROJECT = ROOT / "src/Qyl.Telemetry.AutoInstrumentation/Qyl.Telemetry.AutoInstrumentation.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Net.Http;
using Qyl.Telemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.OpenTelemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(activityListener);

var handler = new CountingHandler();
using var client = new HttpClient(handler);
using (await client.GetAsync("http://qyl.invalid/accepted"))
{
}

using (await client.GetAsync("http://qyl.invalid/unavailable"))
{
}

var httpActivities = captured
    .Where(static activity => activity.TagObjects.Any(static tag =>
        tag.Key == "qyl.instrumentation.domain" &&
        StringComparer.Ordinal.Equals(tag.Value as string, "http.client")))
    .ToArray();
var statuses = httpActivities
    .Select(static activity => Convert.ToString(activity.GetTagItem("http.response.status_code"), CultureInfo.InvariantCulture))
    .OfType<string>()
    .OrderBy(static status => status, StringComparer.Ordinal)
    .ToArray();

Console.WriteLine("client.calls=" + handler.Calls.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.count=" + httpActivities.Length.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.statuses=" + string.Join("|", statuses));

return handler.Calls == 2 && httpActivities.Length == 2 ? 0 : 1;

internal sealed class CountingHandler : HttpMessageHandler
{
    public int Calls { get; private set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Calls++;
        return Task.FromResult(new HttpResponseMessage(Calls == 1 ? HttpStatusCode.NoContent : HttpStatusCode.ServiceUnavailable));
    }
}
'''


EXPECTED = """client.calls=2
activity.count=2
activity.statuses=204|503
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
    <PackageReference Include="Qyl.Telemetry.AutoInstrumentation" Version="{version}" />
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
        "namespace Qyl.Telemetry.AutoInstrumentation.Generated",
        "HttpClient_GetAsync_0",
        "QylInterceptedHttpClient.GetAsync(",
        '"contractKeys":["signals.traces.HTTPCLIENT","signals.metrics.HTTPCLIENT"]',
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
