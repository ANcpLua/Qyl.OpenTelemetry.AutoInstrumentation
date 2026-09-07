#!/usr/bin/env python3
from __future__ import annotations

import platform
import tempfile
from pathlib import Path

from verify_helpers import clean_env, run_checked


ROOT = Path(__file__).resolve().parents[1]
CORE_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation" / "Qyl.Telemetry.AutoInstrumentation.csproj"
GENERATOR_PROJECT = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.csproj"
GENERATOR_DLL = ROOT / "artifacts" / "bin" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators" / "release" / "Qyl.Telemetry.AutoInstrumentation.SourceGenerators.dll"
CORE_TARGETS = ROOT / "src" / "Qyl.Telemetry.AutoInstrumentation" / "buildTransitive" / "Qyl.Telemetry.AutoInstrumentation.targets"
TARGET_FRAMEWORK = "net10.0"
NUGET_ORG = "https://api.nuget.org/v3/index.json"


# The ADO.NET DbCommand lane is what stays an interceptor after 15.0.0: System.Data.Common declares
# no ActivitySource, so a generated call-site interceptor is the only producer there is. The probe
# implements the abstract command itself, so the consumer needs no database and no provider package
# — what is under test is that a ProjectReference consumer gets the generated interceptor at all.
PROGRAM = r'''
using System.Collections;
using System.Data;
using System.Data.Common;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;

var captured = new List<Activity>();
using var activityListener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == "Qyl.Telemetry.AutoInstrumentation",
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(activity),
};

ActivitySource.AddActivityListener(activityListener);

using var command = new ProbeCommand();
_ = command.ExecuteScalar();

Console.WriteLine("command.calls=" + command.Calls.ToString(CultureInfo.InvariantCulture));
Console.WriteLine("activity.count=" + captured.Count.ToString(CultureInfo.InvariantCulture));

if (captured.Count == 1)
{
    var activity = captured[0];
    var tags = activity.TagObjects.ToDictionary(
        static tag => tag.Key,
        static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
        StringComparer.Ordinal);
    tags.TryGetValue("qyl.instrumentation.domain", out var domain);
    tags.TryGetValue("db.system.name", out var system);
    tags.TryGetValue("db.operation.name", out var operation);
    tags.TryGetValue("db.query.summary", out var summary);
    tags.TryGetValue("db.query.text", out var queryText);

    Console.WriteLine("activity.name=" + activity.DisplayName);
    Console.WriteLine("activity.kind=" + activity.Kind);
    Console.WriteLine("qyl.instrumentation.domain=" + domain);
    Console.WriteLine("db.system.name=" + system);
    Console.WriteLine("db.operation.name=" + operation);
    Console.WriteLine("db.query.summary=" + summary);
    Console.WriteLine("db.query.text=" + (queryText ?? "<absent>"));
}

return 0;

internal sealed class ProbeCommand : DbCommand
{
    public int Calls { get; private set; }

    [AllowNull]
    public override string CommandText { get; set; } = "SELECT 1";

    public override int CommandTimeout { get; set; }

    public override CommandType CommandType { get; set; } = CommandType.Text;

    public override bool DesignTimeVisible { get; set; }

    public override UpdateRowSource UpdatedRowSource { get; set; }

    protected override DbConnection? DbConnection { get; set; }

    protected override DbParameterCollection DbParameterCollection { get; } = new ProbeParameterCollection();

    protected override DbTransaction? DbTransaction { get; set; }

    public override void Cancel()
    {
    }

    public override int ExecuteNonQuery() => 0;

    public override object? ExecuteScalar()
    {
        Calls++;
        return null;
    }

    public override void Prepare()
    {
    }

    protected override DbParameter CreateDbParameter() => throw new NotSupportedException();

    protected override DbDataReader ExecuteDbDataReader(CommandBehavior behavior) => throw new NotSupportedException();
}

internal sealed class ProbeParameterCollection : DbParameterCollection
{
    private readonly List<DbParameter> _parameters = [];

    public override int Count => _parameters.Count;

    public override object SyncRoot { get; } = new();

    public override int Add(object value) => throw new NotSupportedException();

    public override void AddRange(Array values) => throw new NotSupportedException();

    public override void Clear() => _parameters.Clear();

    public override bool Contains(object value) => false;

    public override bool Contains(string value) => false;

    public override void CopyTo(Array array, int index) => throw new NotSupportedException();

    public override IEnumerator GetEnumerator() => _parameters.GetEnumerator();

    public override int IndexOf(object value) => -1;

    public override int IndexOf(string parameterName) => -1;

    public override void Insert(int index, object value) => throw new NotSupportedException();

    public override void Remove(object value) => throw new NotSupportedException();

    public override void RemoveAt(int index) => _parameters.RemoveAt(index);

    public override void RemoveAt(string parameterName) => throw new NotSupportedException();

    protected override DbParameter GetParameter(int index) => _parameters[index];

    protected override DbParameter GetParameter(string parameterName) => throw new NotSupportedException();

    protected override void SetParameter(int index, DbParameter value) => _parameters[index] = value;

    protected override void SetParameter(string parameterName, DbParameter value) => throw new NotSupportedException();
}
'''


EXPECTED_VERIFIED = """command.calls=1
activity.count=1
activity.name=SELECT
activity.kind=Client
qyl.instrumentation.domain=db.client
db.system.name=other_sql
db.operation.name=SELECT
db.query.summary=SELECT
db.query.text=<absent>
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
        "Qyl.Telemetry.AutoInstrumentation.Generated",
        "file sealed class InterceptsLocationAttribute",
        "global::ProbeCommand receiver",
        "global::Qyl.Telemetry.AutoInstrumentation.GeneratedCode.QylInterceptedDbCommand.Execute(",
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
