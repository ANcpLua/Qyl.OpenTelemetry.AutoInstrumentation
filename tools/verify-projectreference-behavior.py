#!/usr/bin/env python3
from __future__ import annotations

import platform
import tempfile
from pathlib import Path

from verify_helpers import clean_env, run_checked


ROOT = Path(__file__).resolve().parents[1]
CORE_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj"
GENERATOR_DLL = ROOT / "artifacts" / "bin" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "release" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.dll"
CORE_TARGETS = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "buildTransitive" / "Qyl.OpenTelemetry.AutoInstrumentation.targets"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


PROGRAM = r'''
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using Qyl.OpenTelemetry.AutoInstrumentation;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(activityListener);

var concreteLogger = new CapturingLogger();
ILogger logger = concreteLogger;
logger.Log(
    LogLevel.Information,
    new EventId(42, "projectreference-log"),
    "projectreference-log",
    exception: null,
    static (state, exception) => exception is null ? state : state + ":" + exception.GetType().Name);

Console.WriteLine("logger.calls=" + concreteLogger.Calls.ToString(System.Globalization.CultureInfo.InvariantCulture));
Console.WriteLine("logger.last=" + concreteLogger.Last);
Console.WriteLine("activity.count=" + captured.Count.ToString(System.Globalization.CultureInfo.InvariantCulture));

if (captured.Count == 1)
{
    var activity = captured[0];
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);
    tags.TryGetValue(QylSemanticAttributes.QylInstrumentationDomain, out var domain);
    tags.TryGetValue(QylSemanticAttributes.LogSeverity, out var severity);

    Console.WriteLine("activity.name=" + activity.DisplayName);
    Console.WriteLine("activity.kind=" + activity.Kind);
    Console.WriteLine(QylSemanticAttributes.QylInstrumentationDomain + "=" + domain);
    Console.WriteLine(QylSemanticAttributes.LogSeverity + "=" + severity);
}

return 0;

internal sealed class CapturingLogger : ILogger
{
    public int Calls { get; private set; }

    public string Last { get; private set; } = string.Empty;

    public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

    public bool IsEnabled(LogLevel logLevel) => true;

    public void Log<TState>(
        LogLevel logLevel,
        EventId eventId,
        TState state,
        Exception? exception,
        Func<TState, Exception?, string> formatter)
    {
        Calls++;
        Last = logLevel + ":" + eventId.Id.ToString(System.Globalization.CultureInfo.InvariantCulture) + ":" + formatter(state, exception);
    }
}
'''


EXPECTED_VERIFIED = """logger.calls=1
logger.last=Information:42:projectreference-log
activity.count=1
activity.name=ILogger log
activity.kind=Internal
qyl.instrumentation.domain=log.ilogger
log.severity=Information
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"

    fail(f"unsupported NativeAOT gate platform: {platform.system()} {platform.machine()}")


def native_executable_name() -> str:
    return "Consumer.exe" if platform.system().lower() == "windows" else "Consumer"


def write_project(directory: Path, packages: Path) -> Path:
    directory.mkdir(parents=True)
    project_path = directory / "Consumer.csproj"
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{CORE_PROJECT}" />
    <ProjectReference Include="{GENERATOR_PROJECT}"
                      Condition="'$(PublishAot)' != 'true'"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false"
                      GlobalPropertiesToRemove="PublishAot;PublishSingleFile;PublishTrimmed;RuntimeIdentifier;RuntimeIdentifiers;SelfContained" />
    <Analyzer Include="{GENERATOR_DLL}" Condition="'$(PublishAot)' == 'true'" />
    <PackageReference Include="Microsoft.Extensions.Logging.Abstractions" Version="10.0.8" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>

  <Import Project="{CORE_TARGETS}" />
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def verify_verified(label: str, stdout: str) -> None:
    if stdout != EXPECTED_VERIFIED:
        fail(f"{label} output mismatch\nexpected:\n{EXPECTED_VERIFIED}\nactual:\n{stdout}")


def verify_generated_interceptor_source(directory: Path) -> None:
    generated_files = sorted((directory / "Generated").rglob("QylAutoInstrumentation.Interceptors.g.cs"))
    if len(generated_files) != 1:
        fail(f"expected exactly one generated interceptor source file, found {len(generated_files)}")

    text = generated_files[0].read_text(encoding="utf-8")
    for token in [
        "#nullable enable",
        "Qyl.OpenTelemetry.AutoInstrumentation.Generated",
        "file sealed class InterceptsLocationAttribute",
        "global::Microsoft.Extensions.Logging.ILogger",
    ]:
        if token not in text:
            fail(f"generated interceptor source missing token: {token}")


def verify_managed(project: Path, directory: Path, env: dict[str, str]) -> None:
    run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], directory, env)
    verify_generated_interceptor_source(directory)

    app_dll = directory / "bin" / "Release" / TARGET_FRAMEWORK / "Consumer.dll"
    completed = run_checked(["dotnet", str(app_dll)], directory, env)
    verify_verified("managed ProjectReference consumer", completed.stdout)


def verify_nativeaot(project: Path, directory: Path, env: dict[str, str]) -> None:
    rid = runtime_identifier()
    run_checked(
        [
            "dotnet",
            "publish",
            str(project),
            "-c",
            "Release",
            "-r",
            rid,
            "-p:PublishAot=true",
            "-p:SelfContained=true",
            "-p:InvariantGlobalization=true",
            "-v",
            "quiet",
        ],
        directory,
        env,
    )

    native_app = directory / "bin" / "Release" / TARGET_FRAMEWORK / rid / "publish" / native_executable_name()
    completed = run_checked([str(native_app)], directory, env)
    verify_verified("NativeAOT ProjectReference consumer", completed.stdout)


def main() -> None:
    env = clean_env()
    run_checked(["dotnet", "build", str(GENERATOR_PROJECT), "-c", "Release", "-v", "quiet"], ROOT, env)
    with tempfile.TemporaryDirectory(prefix="qyl-projectreference-consumer-") as temp:
        directory = Path(temp) / "consumer"
        packages = Path(temp) / "packages"
        project = write_project(directory, packages)
        verify_managed(project, directory, env)
        verify_nativeaot(project, directory, env)

    print("projectreference-behavior-ok")


if __name__ == "__main__":
    main()
