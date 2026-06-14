#!/usr/bin/env python3
from __future__ import annotations

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
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"
DIAGNOSTIC_LISTENERS_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners" / "Qyl.AutoInstrumentation.DiagnosticListeners.csproj"
HOSTING_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.Hosting" / "Qyl.AutoInstrumentation.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Qyl.AutoInstrumentation;
using Qyl.AutoInstrumentation.Hosting;

var mode = args.Length is 0 ? "direct" : args[0];
var checks = 0L;

using var meterListener = new MeterListener();
meterListener.InstrumentPublished = static (instrument, listener) =>
{
    if (instrument.Meter.Name == QylSelfTelemetry.MeterName &&
        instrument.Name == QylMetricNames.QylSemConvAttributeChecks)
    {
        listener.EnableMeasurementEvents(instrument);
    }
};
meterListener.SetMeasurementEventCallback<long>(
    (instrument, measurement, tags, state) => checks += measurement);
meterListener.Start();

if (mode == "hosting")
{
    new ServiceCollection().AddQylAutoInstrumentation(static options => options.EnableConformanceProcessor = true);
}
else
{
    QylInstrumentation.Activate();
}

using (var activity = QylActivitySource.Source.StartActivity("conformance probe"))
{
    activity?.SetTag(QylSemanticAttributes.QylInstrumentationDomain, "probe");
}

Console.WriteLine("mode=" + mode);
Console.WriteLine("hasListeners=" + QylActivitySource.Source.HasListeners().ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("checks=" + checks.ToString(System.Globalization.CultureInfo.InvariantCulture));
'''


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in [CORE_PROJECT, DIAGNOSTIC_LISTENERS_PROJECT, HOSTING_PROJECT]:
                run_checked(
                    ["dotnet", "pack", str(project), "-c", "Release", "-o", str(feed), "-v", "quiet"],
                    ROOT,
                    env,
                )
        finally:
            if fcntl is not None:
                fcntl.flock(lock, fcntl.LOCK_UN)


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
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Microsoft.Extensions.DependencyInjection" Version="10.0.8" />
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def run_scenario(assembly: Path, base_env: dict[str, str], mode: str, overrides: dict[str, str]) -> str:
    env = dict(base_env)
    env.update(overrides)
    completed = subprocess.run(
        ["dotnet", str(assembly), mode],
        cwd=assembly.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(f"{mode} scenario failed\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}")
    if completed.stderr:
        fail(f"{mode} scenario wrote stderr:\n{completed.stderr}")

    return completed.stdout


def assert_output(name: str, actual: str, expected: str) -> None:
    if actual != expected:
        fail(f"{name} mismatch\nEXPECTED\n{expected}\nACTUAL\n{actual}")


def main() -> None:
    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-conformance-opt-in-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], project.parent, env)
        assembly = project.parent / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"

        assert_output("default off", run_scenario(assembly, env, "direct", {}), "mode=direct\nhasListeners=False\nchecks=0\n")
        assert_output(
            "environment opt-in",
            run_scenario(assembly, env, "direct", {"QYL_CONFORMANCE_ENABLED": "true"}),
            "mode=direct\nhasListeners=True\nchecks=1\n",
        )
        assert_output(
            "hosting opt-in",
            run_scenario(assembly, env, "hosting", {}),
            "mode=hosting\nhasListeners=True\nchecks=1\n",
        )

    print("conformance-opt-in-ok")


if __name__ == "__main__":
    main()
