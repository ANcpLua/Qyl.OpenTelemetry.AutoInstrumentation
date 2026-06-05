#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import os
import platform
import re
import subprocess
import tempfile
from pathlib import Path

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
PROPS_PATH = ROOT / "Directory.Build.props"
GOLDEN_PATH = ROOT / "tools" / "Qyl.AutoInstrumentation.WebApiAotDemo" / "golden" / "report.json"
NUGET_ORG = "https://api.nuget.org/v3/index.json"
TARGET_FRAMEWORK = "net10.0"

PROJECTS_TO_PACK = [
    ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj",
    ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners" / "Qyl.AutoInstrumentation.DiagnosticListeners.csproj",
    ROOT / "src" / "Qyl.AutoInstrumentation.Hosting" / "Qyl.AutoInstrumentation.Hosting.csproj",
    ROOT / "src" / "Qyl.AutoInstrumentation.EntityFrameworkCore" / "Qyl.AutoInstrumentation.EntityFrameworkCore.csproj",
    ROOT / "src" / "Qyl.AutoInstrumentation.SqlClient" / "Qyl.AutoInstrumentation.SqlClient.csproj",
]

EFCORE_COMPILED_MODEL_SOURCES = [
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "ProbeContext.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextAssemblyAttributes.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextModel.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeContextModelBuilder.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeItemEntityType.cs",
    ROOT / "demos" / "Qyl.RealEfCoreDemo" / "CompiledModels" / "ProbeItemUnsafeAccessors.cs",
]

PROGRAM = r'''
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Qyl.AutoInstrumentation;
using Qyl.RealEfCoreDemo;

var captured = new List<CapturedActivity>();
using var listener = new ActivityListener
{
    ShouldListenTo = static source => source.Name == QylActivitySource.Name,
    Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
    ActivityStopped = activity => captured.Add(CapturedActivity.From(activity)),
};

ActivitySource.AddActivityListener(listener);

using var downstream = new HttpClient(new StubHandler())
{
    BaseAddress = new Uri("https://qyl-webapi.invalid"),
};
using (await downstream.GetAsync("/downstream?secret=redacted"))
{
}

await using (var sqlConnection = new SqlConnection())
{
    await using var sqlCommand = sqlConnection.CreateCommand();
    sqlCommand.CommandText = "SELECT 1";
    try
    {
        _ = await sqlCommand.ExecuteScalarAsync();
    }
    catch (InvalidOperationException)
    {
    }
}

await using var sqlite = new SqliteConnection("Data Source=:memory:");
await sqlite.OpenAsync();
await CreateSchemaAsync(sqlite);

var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls("http://127.0.0.1:0");
builder.WebHost.SuppressStatusMessages(true);
builder.Logging.ClearProviders();
var app = builder.Build();

app.MapGet("/probe/{id:int}", async () =>
{
    await using (var db = new ProbeContext(sqlite))
    {
        await db.Database.ExecuteSqlRawAsync("INSERT INTO Items (Name) VALUES ('webapi')");
    }

    return Results.NoContent();
});

await app.StartAsync();

try
{
    var address = app.Urls.Single();
    using var client = new HttpClient();
    using (await client.GetAsync(address + "/probe/42?secret=redacted"))
    {
    }
}
finally
{
    await app.StopAsync();
}

var report = WebApiAotReport.Create(captured.ToArray());
var json = JsonSerializer.Serialize(report, WebApiAotJsonContext.Default.WebApiAotReport);
Console.WriteLine(json);

return report.Pass ? 0 : 1;

static async Task CreateSchemaAsync(SqliteConnection connection)
{
    await using var command = connection.CreateCommand();
    command.CommandText = """
        CREATE TABLE Items (
            Id INTEGER NOT NULL CONSTRAINT PK_Items PRIMARY KEY AUTOINCREMENT,
            Name TEXT NOT NULL
        );
        """;
    await command.ExecuteNonQueryAsync();
}

internal sealed class StubHandler : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
        {
            RequestMessage = request,
        });
}

internal sealed record CapturedActivity(
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags)
{
    public static CapturedActivity From(Activity activity)
        => new(
            activity.DisplayName,
            activity.Kind.ToString(),
            activity.Status.ToString(),
            activity.TagObjects.ToDictionary(
                static tag => tag.Key,
                static tag => Convert.ToString(tag.Value, CultureInfo.InvariantCulture) ?? string.Empty,
                StringComparer.Ordinal));
}

internal sealed record MatchedSignal(
    string Signal,
    string Name,
    string Kind,
    string Status,
    IReadOnlyDictionary<string, string> Tags);

internal sealed record WebApiAotReport(
    string RuntimeMode,
    bool Pass,
    string[] Failures,
    MatchedSignal[] Signals,
    MatchedSignal[] Activities)
{
    public static WebApiAotReport Create(CapturedActivity[] activities)
    {
        var failures = new List<string>();
        var signals = new List<MatchedSignal>();

        AddRequired(signals, failures, "aspnetcore.server", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.server") &&
            HasTag(activity, "http.route", "/probe/{id:int}")));

        AddRequired(signals, failures, "httpclient.self", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "server.address", "127.0.0.1")));

        AddRequired(signals, failures, "httpclient.downstream", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "http.client") &&
            HasTag(activity, "http.request.method", "GET") &&
            HasTag(activity, "http.response.status_code", "204") &&
            !activity.Tags.ContainsKey("server.address")));

        AddRequired(signals, failures, "efcore.sqlite", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.efcore")));

        AddRequired(signals, failures, "sqlclient.command", activities.FirstOrDefault(static activity =>
            HasTag(activity, "qyl.instrumentation.domain", "db.sqlclient") &&
            HasTag(activity, "db.operation.name", "SELECT") &&
            HasTag(activity, "error.type", "System.InvalidOperationException")));

        foreach (var signal in signals)
        {
            if (signal.Tags.ContainsKey("url.full") ||
                signal.Tags.ContainsKey("url.path") ||
                signal.Tags.ContainsKey("db.query.text"))
            {
                failures.Add("sensitive raw value leaked in " + signal.Signal);
            }
        }

        return new WebApiAotReport(
            RuntimeFeature.IsDynamicCodeSupported ? "dynamic-code-supported" : "nativeaot",
            failures.Count is 0,
            failures.ToArray(),
            signals.OrderBy(static signal => signal.Signal, StringComparer.Ordinal).ToArray(),
            activities
                .Select(static activity => new MatchedSignal("activity", activity.Name, activity.Kind, activity.Status, Canonicalize(activity.Tags)))
                .OrderBy(static signal => signal.Name, StringComparer.Ordinal)
                .ThenBy(static signal => signal.Kind, StringComparer.Ordinal)
                .ThenBy(static signal => string.Join(",", signal.Tags.Select(static pair => pair.Key + "=" + pair.Value)), StringComparer.Ordinal)
                .ToArray());
    }

    private static void AddRequired(
        ICollection<MatchedSignal> signals,
        ICollection<string> failures,
        string signal,
        CapturedActivity? activity)
    {
        if (activity is null)
        {
            failures.Add("missing " + signal);
            return;
        }

        signals.Add(new MatchedSignal(signal, activity.Name, activity.Kind, activity.Status, Canonicalize(activity.Tags)));
    }

    private static bool HasTag(CapturedActivity activity, string key, string expected)
        => activity.Tags.TryGetValue(key, out var actual) &&
           StringComparer.Ordinal.Equals(actual, expected);

    private static IReadOnlyDictionary<string, string> Canonicalize(IReadOnlyDictionary<string, string> tags)
    {
        var keep = new[]
        {
            "qyl.instrumentation.domain",
            "http.request.method",
            "http.route",
            "http.response.status_code",
            "server.address",
            "server.port",
            "db.system",
            "db.operation.name",
            "db.query.summary",
            "error.type",
        };
        var result = new SortedDictionary<string, string>(StringComparer.Ordinal);
        foreach (var key in keep)
        {
            if (!tags.TryGetValue(key, out var value))
                continue;

            result[key] = key is "server.port" ? "<port>" : value;
        }

        return result;
    }
}

[JsonSerializable(typeof(WebApiAotReport))]
[JsonSourceGenerationOptions(WriteIndented = true)]
internal sealed partial class WebApiAotJsonContext : JsonSerializerContext;
'''


def fail(message: str) -> None:
    raise SystemExit(message)


def clean_env() -> dict[str, str]:
    env = dict(os.environ)
    for key in list(env):
        if key.startswith("OTEL_") or key.startswith("QYL_"):
            del env[key]

    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    return env


def read_version() -> str:
    text = PROPS_PATH.read_text(encoding="utf-8")
    prefix = "<Version>"
    suffix = "</Version>"
    start = text.find(prefix)
    if start < 0:
        fail("Directory.Build.props is missing <Version>")

    end = text.find(suffix, start)
    if end < 0:
        fail("Directory.Build.props has unterminated <Version>")

    return text[start + len(prefix):end].strip()


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"

    fail(f"unsupported NativeAOT web API gate platform: {platform.system()} {platform.machine()}")


def run_checked(command: list[str], cwd: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    completed = subprocess.run(
        command,
        cwd=cwd,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(
            "command failed: "
            + " ".join(command)
            + f"\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )

    return completed


def pack_runtime(feed: Path, env: dict[str, str]) -> None:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            for project in PROJECTS_TO_PACK:
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
    project_path = directory / "WebApiAotDemo.csproj"
    compile_items = "\n".join(
        f'    <Compile Include="{path}" Link="EfCoreCompiledModel/{path.name}" />'
        for path in EFCORE_COMPILED_MODEL_SOURCES
    )
    project_path.write_text(
        f'''<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RestoreSources>{feed};{NUGET_ORG}</RestoreSources>
    <RestorePackagesPath>{packages}</RestorePackagesPath>
    <RestoreNoCache>true</RestoreNoCache>
    <DefineConstants>$(DefineConstants);USE_COMPILED_MODEL</DefineConstants>
  </PropertyGroup>

  <ItemGroup>
    <PackageReference Include="Qyl.AutoInstrumentation.Hosting" Version="{version}" />
    <PackageReference Include="Qyl.AutoInstrumentation.EntityFrameworkCore" Version="{version}" />
    <PackageReference Include="Qyl.AutoInstrumentation.SqlClient" Version="{version}" />
    <PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="10.0.8" />
    <PackageReference Include="Microsoft.Data.SqlClient" Version="7.0.1" />
  </ItemGroup>

  <ItemGroup>
{compile_items}
  </ItemGroup>
</Project>
''',
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project_path


def publish_nativeaot(project: Path, output: Path, log: Path, env: dict[str, str]) -> Path:
    completed = subprocess.run(
        [
            "dotnet",
            "publish",
            str(project),
            "-c",
            "Release",
            "-r",
            runtime_identifier(),
            "-p:PublishAot=true",
            "-p:TreatWarningsAsErrors=false",
            "--self-contained",
            "true",
            "-o",
            str(output),
            "-v",
            "quiet",
        ],
        cwd=project.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.STDOUT,
        check=False,
    )
    log.write_text(completed.stdout, encoding="utf-8")
    if completed.returncode != 0:
        fail(
            "NativeAOT web API publish failed\n"
            f"exit={completed.returncode}\nlog={log}\n{completed.stdout}"
        )

    qyl_warnings = [
        line for line in completed.stdout.splitlines()
        if re.search(r"\b(?:IL2[0-9]{3}|IL3[0-9]{3}|IL4[0-9]{3}|CA[0-9]{4})\b", line) and
        "Qyl.AutoInstrumentation" in line
    ]
    if qyl_warnings:
        fail("NativeAOT web API publish emitted qyl-owned analyzer warnings:\n" + "\n".join(qyl_warnings))

    executable = output / ("WebApiAotDemo.exe" if platform.system().lower() == "windows" else "WebApiAotDemo")
    if not executable.exists():
        fail(f"NativeAOT web API executable missing: {executable}")

    return executable


def run_executable(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def verify_or_update_golden(stdout: str, update: bool) -> None:
    parsed = json.loads(stdout)
    canonical = json.dumps(parsed, indent=2, sort_keys=True) + "\n"
    if update:
        GOLDEN_PATH.parent.mkdir(parents=True, exist_ok=True)
        GOLDEN_PATH.write_text(canonical, encoding="utf-8")
        return

    if not GOLDEN_PATH.exists():
        fail(f"missing web API AOT golden: {GOLDEN_PATH}")

    expected = GOLDEN_PATH.read_text(encoding="utf-8")
    if canonical != expected:
        received = GOLDEN_PATH.with_suffix(".received.json")
        received.write_text(canonical, encoding="utf-8")
        fail(
            "web API AOT golden mismatch\n"
            f"expected={GOLDEN_PATH}\n"
            f"received={received}"
        )


def main() -> None:
    parser = argparse.ArgumentParser(description="Publish and run the NativeAOT web API instrumentation demo.")
    parser.add_argument("--update-golden", action="store_true", help="Update the committed canonical output.")
    args = parser.parse_args()

    env = clean_env()
    version = read_version()
    with tempfile.TemporaryDirectory(prefix="qyl-webapi-aot-demo-") as temp:
        root = Path(temp)
        feed = root / "feed"
        packages = root / "packages"
        publish = root / "publish"
        publish_log = root / "publish.log"
        pack_runtime(feed, env)
        project = write_project(root / "consumer", feed, packages, version)
        executable = publish_nativeaot(project, publish, publish_log, env)
        completed = run_executable(executable, env)

    if completed.returncode != 0:
        fail(
            "NativeAOT web API demo failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"NativeAOT web API demo wrote stderr:\n{completed.stderr}")

    verify_or_update_golden(completed.stdout, args.update_golden)
    print("webapi-aot-demo-ok qyl_warnings=0")


if __name__ == "__main__":
    main()
