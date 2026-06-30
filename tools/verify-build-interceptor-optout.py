#!/usr/bin/env python3
"""Verifies the WebApplicationBuilder.Build() interceptor opt-out.

A consumer can hand WebApplicationBuilder.Build() interception to a cooperating generator
(for example qyl's ServiceDefaults generator, which composes QylInterceptedAspNetCore.Build)
by setting <QylAutoInstrumentationInterceptWebApplicationBuilderBuild>false</...>. C# forbids
two interceptors on one call site, so without this opt-out the two generators collide (CS9153).

  default build  -> the Build() interceptor IS emitted (full auto-instrumentation).
  opt-out build  -> the Build() interceptor is NOT emitted, and the build still succeeds.

The opt-out is surgical: only the WebApplicationBuilder.Build() interceptor is withheld; every
other interceptor in the same consumer is unaffected.
"""
from __future__ import annotations

import tempfile
from pathlib import Path

from verify_helpers import clean_env, run_checked


ROOT = Path(__file__).resolve().parents[1]
CORE_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
GENERATOR_PROJECT = (
    ROOT
    / "src"
    / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators"
    / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj"
)
TARGETS = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "buildTransitive" / "Qyl.OpenTelemetry.AutoInstrumentation.targets"
TARGET_FRAMEWORK = "net10.0"
OPT_OUT_PROPERTY = "QylAutoInstrumentationInterceptWebApplicationBuilderBuild"
BUILD_INTERCEPTOR_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedAspNetCore.Build("
OTHER_INTERCEPTOR_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedHttpClient.GetStringAsync("

# A WebApplicationBuilder.Build() call (intercepted by AspNetCoreWebApplicationBuilderBuild) plus a
# never-executed HttpClient call (intercepted by HttpClient) so the opt-out build still emits at
# least one interceptor — proving the opt-out drops only Build(), not every interceptor.
PROGRAM = """using System.Net.Http;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "ok");
app.Run();

static async System.Threading.Tasks.Task Probe(HttpClient client)
    => _ = await client.GetStringAsync("https://qyl-build-optout.invalid");
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def write_project(directory: Path) -> Path:
    directory.mkdir(parents=True)
    project = directory / "BuildOptOutConsumer.csproj"
    project.write_text(
        f"""<Project Sdk="Microsoft.NET.Sdk.Web">
  <PropertyGroup>
    <TargetFramework>{TARGET_FRAMEWORK}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <EmitCompilerGeneratedFiles>true</EmitCompilerGeneratedFiles>
    <CompilerGeneratedFilesOutputPath>Generated</CompilerGeneratedFilesOutputPath>
  </PropertyGroup>

  <ItemGroup>
    <ProjectReference Include="{CORE_PROJECT}" />
    <ProjectReference Include="{GENERATOR_PROJECT}"
                      OutputItemType="Analyzer"
                      ReferenceOutputAssembly="false" />
    <Compile Remove="Generated/**/*.cs" />
  </ItemGroup>

  <Import Project="{TARGETS}" />
</Project>
""",
        encoding="utf-8",
    )
    (directory / "Program.cs").write_text(PROGRAM, encoding="utf-8")
    return project


def generated_interceptor_text(directory: Path) -> str:
    files = sorted((directory / "Generated").rglob("QylAutoInstrumentation.Interceptors.g.cs"))
    if not files:
        return ""
    if len(files) != 1:
        fail(f"expected at most one generated interceptor source file, found {len(files)}")
    return files[0].read_text(encoding="utf-8")


def build(project: Path, env: dict[str, str], opt_out: bool) -> None:
    command = ["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"]
    if opt_out:
        command.append(f"-p:{OPT_OUT_PROPERTY}=false")
    run_checked(command, project.parent, env)


def main() -> None:
    env = clean_env()
    with tempfile.TemporaryDirectory(prefix="qyl-build-optout-") as temp:
        default_dir = Path(temp) / "default"
        build(write_project(default_dir), env, opt_out=False)
        default_text = generated_interceptor_text(default_dir)
        if BUILD_INTERCEPTOR_TOKEN not in default_text:
            fail("default build did not emit the WebApplicationBuilder.Build() interceptor")
        if OTHER_INTERCEPTOR_TOKEN not in default_text:
            fail("default build did not emit the control HttpClient interceptor")

        optout_dir = Path(temp) / "optout"
        build(write_project(optout_dir), env, opt_out=True)
        optout_text = generated_interceptor_text(optout_dir)
        if BUILD_INTERCEPTOR_TOKEN in optout_text:
            fail("opt-out build still emitted the WebApplicationBuilder.Build() interceptor")
        if OTHER_INTERCEPTOR_TOKEN not in optout_text:
            fail("opt-out build dropped more than Build() — the control HttpClient interceptor is gone")

    print("build-interceptor-optout-ok")


if __name__ == "__main__":
    main()
