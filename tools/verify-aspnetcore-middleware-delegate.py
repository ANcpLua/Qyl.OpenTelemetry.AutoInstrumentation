#!/usr/bin/env python3
"""Verifies hand-written ASP.NET Core middleware does not break the build.

Convention-based middleware invokes the next hop as `next(context)` / `_next(context)`,
which the C# semantic model resolves to `RequestDelegate.Invoke`. That symbol has
`MethodKind.DelegateInvoke`, and the C# interceptors feature can only intercept *ordinary*
member methods — intercepting a delegate invocation is rejected with CS9207
("Cannot intercept 'next' because it is not an invocation of an ordinary member method").

The generator must not emit an `AspNetCoreRequestDelegate` interceptor for these call sites.
The verifier builds a consumer with `next(context)` middleware and an unrelated HttpClient
control, proving the generator skips delegate invocations while still emitting supported
interceptors.
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
REQUEST_DELEGATE_INTERCEPTOR_TOKEN = "AspNetCoreRequestDelegate_Invoke"
CONTROL_INTERCEPTOR_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedHttpClient.GetStringAsync("

# Convention-based middleware whose next-hop call is a delegate invocation (`next(context)`),
# plus a never-executed HttpClient call as a control so we can prove the generator still emits
# interceptors for ordinary methods and only withholds the un-interceptable delegate invocation.
PROGRAM = """using System.Net.Http;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;

var builder = WebApplication.CreateSlimBuilder(args);
var app = builder.Build();
app.UseMiddleware<PassThroughMiddleware>();
app.MapGet("/", () => "ok");
app.Run();

static async System.Threading.Tasks.Task Probe(HttpClient client)
    => _ = await client.GetStringAsync("https://qyl-middleware-delegate.invalid");

internal sealed class PassThroughMiddleware(RequestDelegate next)
{
    // `next(context)` is a RequestDelegate (delegate) invocation, not an ordinary member call,
    // so the interceptors feature cannot intercept it (CS9207). The generator must skip it.
    public System.Threading.Tasks.Task InvokeAsync(HttpContext context) => next(context);
}
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def write_project(directory: Path) -> Path:
    directory.mkdir(parents=True)
    project = directory / "MiddlewareDelegateConsumer.csproj"
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


def main() -> None:
    env = clean_env()
    with tempfile.TemporaryDirectory(prefix="qyl-middleware-delegate-") as temp:
        consumer = Path(temp) / "consumer"
        # A delegate-invoke interceptor would fail this build with CS9207; a clean build is the
        # primary assertion (run_checked raises on non-zero exit).
        run_checked(
            ["dotnet", "build", str(write_project(consumer)), "-c", "Release", "-v", "quiet"],
            consumer,
            env,
        )
        text = generated_interceptor_text(consumer)
        if REQUEST_DELEGATE_INTERCEPTOR_TOKEN in text:
            fail("generator emitted an un-interceptable RequestDelegate.Invoke interceptor (CS9207 risk)")
        if CONTROL_INTERCEPTOR_TOKEN not in text:
            fail("generator emitted no control HttpClient interceptor — it went silent instead of selective")

    print("aspnetcore-middleware-delegate-ok")


if __name__ == "__main__":
    main()
