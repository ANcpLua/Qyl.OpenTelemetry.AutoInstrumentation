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
using System.Net;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};
ActivitySource.AddActivityListener(listener);

using var http = new HttpClient(new StubHandler())
{
    BaseAddress = new Uri("https://qyl-disabled.invalid"),
};
using (await http.GetAsync("/probe"))
{
}

var httpClientSpans = captured.Count(static activity =>
    activity.TagObjects.Any(static tag =>
        tag.Key == QylSemanticAttributes.QylInstrumentationDomain &&
        string.Equals(
            Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture),
            QylInstrumentationDomains.HttpClient,
            StringComparison.Ordinal)));

Console.WriteLine("httpclient.spans=" + httpClientSpans.ToString(System.Globalization.CultureInfo.InvariantCulture));
return 0;

internal sealed class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            RequestMessage = request,
        });
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

        # Control: enabled by default -> exactly one HttpClient span.
        assert_spans("enabled (control)", run_scenario(assembly, env, {}), "httpclient.spans=1")
        # Disabled per-signal -> zero spans.
        assert_spans(
            "http instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_TRACES_HTTPCLIENT_INSTRUMENTATION_ENABLED": "false"}),
            "httpclient.spans=0",
        )
        # Disabled globally -> zero spans.
        assert_spans(
            "global instrumentation disabled",
            run_scenario(assembly, env, {"OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED": "false"}),
            "httpclient.spans=0",
        )

    print("instrumentation-disabled-behavior-ok")


if __name__ == "__main__":
    main()
