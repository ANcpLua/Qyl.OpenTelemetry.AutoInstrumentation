#!/usr/bin/env python3
"""AddQyl behaviour gate: registered once, and never blocking on a socket.

Three claims, all observable from outside the SDK:

* **Idempotent.** A composing library and the application both call `AddQyl()` — `AddQylApi` is the
  real case. Each call used to queue another `WithTracing`/`WithMetrics` callback onto the same
  provider, so the pipeline ended up with two `BatchExportProcessor` + `OtlpExporter` pairs and two
  of each qyl processor, and every span left the process twice. The proof is the count of OTLP
  requests a listener receives for one span: one, not two.
* **Non-blocking.** Collector discovery costs four 100 ms TCP probes plus a DNS lookup for `qyl`
  when nothing is listening. `AddQyl()` must return without paying it.
* **Deduplicating.** A consumer naming a source qyl already subscribes — `System.Net.Http` is the
  one they reach for — must not subscribe it twice.
* **Silent at build time.** GetDocument.Insider runs the application's startup path at build time,
  inside the developer's environment, `OTEL_EXPORTER_OTLP_ENDPOINT` included. It must reach no
  collector. The proof is the same listener seeing zero requests from that host while the ordinary
  host under the same variable exports.

The fixture runs the real registration path against a local OTLP/HTTP listener, so nothing here
reaches into SDK internals.
"""
from __future__ import annotations

import json
import shutil
import subprocess
from pathlib import Path
from typing import Any

from verify_helpers import clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
HOSTING_PROJECT = ROOT / "src" / "Qyl.Telemetry.Hosting" / "Qyl.Telemetry.Hosting.csproj"
TARGET_FRAMEWORK = "net10.0"

# Four 100 ms probes plus DNS is ~400 ms. The fixture warms the registration path up with discovery
# disabled first, so the timed call pays the probe and nothing else; this bound is far below the
# probe's cost and far above the registration work that remains.
ADDQYL_BUDGET_MILLISECONDS = 100

PROGRAM = r'''
using System.Diagnostics;
using System.Globalization;
using System.Net;
using System.Text.Json;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using Qyl;

// A local OTLP/HTTP endpoint that counts the export requests it receives.
var listener = new HttpListener();
var port = GetFreePort();
listener.Prefixes.Add($"http://127.0.0.1:{port}/");
listener.Start();
var exportRequests = 0;
var listening = Task.Run(async () =>
{
    while (listener.IsListening)
    {
        HttpListenerContext context;
        try { context = await listener.GetContextAsync(); } catch { return; }
        if (context.Request.Url!.AbsolutePath.EndsWith("/v1/traces", StringComparison.Ordinal))
            Interlocked.Increment(ref exportRequests);
        context.Response.StatusCode = 200;
        context.Response.Close();
    }
});

// 1. AddQyl with discovery on and nothing listening on the conventional ports: it must not wait for
//    the probe. Measured on a builder of its own, because the marker makes a second call on the
//    same builder a no-op.
//
//    The warm-up matters and is not a fudge: the first AddQyl in a process also loads and jits the
//    OpenTelemetry registration path, which costs several hundred milliseconds on its own and would
//    swamp the thing under test. The warm-up runs with discovery OFF, so it pays the assembly cost
//    without touching the probe; the timed call is then the first one that starts the probe, and
//    its elapsed time is the probe's cost and nothing else.
var warmup = NewBuilder();
warmup.AddQyl(o =>
{
    o.ServiceName = "qyl-addqyl-warmup";
    o.EnableCollectorDiscovery = false;
    o.CollectorEndpoint = new Uri("http://127.0.0.1:1");
});

var timedBuilder = NewBuilder();
var stopwatch = Stopwatch.StartNew();
timedBuilder.AddQyl(o => o.ServiceName = "qyl-addqyl-timing");
stopwatch.Stop();
var addQylMilliseconds = stopwatch.Elapsed.TotalMilliseconds;

// 2. Two calls, one pipeline. The second call also asks for a different service name and an extra
//    source; first call wins, and nothing it registered is duplicated.
var builder = NewBuilder();
builder.AddQyl(o =>
{
    o.ServiceName = "qyl-addqyl-behavior";
    o.CollectorEndpoint = new Uri($"http://127.0.0.1:{port}");
    o.EnableCollectorDiscovery = false;
    o.EnableLogExport = false;
    o.EnableMetricsExport = false;
    o.EnableSessionPropagation = false;
    // 3. A source qyl already subscribes, named again by the consumer.
    o.AdditionalSources.Add("System.Net.Http");
});
builder.AddQyl(o => o.ServiceName = "second-call-must-be-ignored");

using var host = builder.Build();
await host.StartAsync();

using (var source = new ActivitySource("Qyl.Telemetry.AutoInstrumentation"))
using (var activity = source.StartActivity("addqyl-probe", ActivityKind.Internal))
{
}

host.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
await host.StopAsync();
await Task.Delay(250);
var exportsAfterNormalHost = Volatile.Read(ref exportRequests);

// 4. The same pipeline under the build-time document host, pointed at the listener BOTH ways: the
//    environment variable it inherits from the developer, and an endpoint configured in code, which
//    is what Qyl.Api does. Neither may produce a single request. Asserting the explicit endpoint too
//    is deliberate: it is the stronger claim, and without it the scenario passes even when the gate
//    is removed, because a null endpoint leaves the exporter on a default nothing is listening on.
Environment.SetEnvironmentVariable("OTEL_EXPORTER_OTLP_ENDPOINT", $"http://127.0.0.1:{port}");
var buildTime = new HostApplicationBuilder(new HostApplicationBuilderSettings
{
    ApplicationName = "GetDocument.Insider",
    DisableDefaults = true,
});
buildTime.AddQyl(o =>
{
    o.EnableCollectorDiscovery = false;
    o.CollectorEndpoint = new Uri($"http://127.0.0.1:{port}");
});
using (var buildTimeHost = buildTime.Build())
{
    await buildTimeHost.StartAsync();
    using (var source = new ActivitySource("Qyl.Telemetry.AutoInstrumentation"))
    using (var activity = source.StartActivity("build-time-probe", ActivityKind.Internal))
    {
    }

    buildTimeHost.Services.GetRequiredService<TracerProvider>().ForceFlush(5_000);
    await buildTimeHost.StopAsync();
}

await Task.Delay(250);
listener.Stop();

var report = new Dictionary<string, object>
{
    ["AddQylMilliseconds"] = Math.Round(addQylMilliseconds, 1),
    ["ExportRequests"] = exportsAfterNormalHost,
    ["BuildTimeExportRequests"] = Volatile.Read(ref exportRequests) - exportsAfterNormalHost,
};
Console.WriteLine(JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true }));
return 0;

static HostApplicationBuilder NewBuilder()
    => new(new HostApplicationBuilderSettings { ApplicationName = "Qyl.AddQylBehavior", DisableDefaults = true });

static int GetFreePort()
{
    var probe = new System.Net.Sockets.TcpListener(IPAddress.Loopback, 0);
    probe.Start();
    var port = ((IPEndPoint)probe.LocalEndpoint).Port;
    probe.Stop();
    return port;
}
'''

PROJECT = """<Project Sdk="Microsoft.NET.Sdk">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>{tfm}</TargetFramework>
    <Nullable>enable</Nullable>
    <ImplicitUsings>enable</ImplicitUsings>
    <RootNamespace>Qyl.AddQylBehavior</RootNamespace>
    <AssemblyName>Qyl.AddQylBehavior</AssemblyName>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="{hosting}" />
  </ItemGroup>
</Project>
"""


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"AddQyl behaviour fixture emitted no JSON report\nstdout={stdout}")
    try:
        return json.loads(stdout[start:])
    except json.JSONDecodeError as error:
        fail(f"AddQyl behaviour fixture emitted invalid JSON: {error}\nstdout={stdout}")
    raise AssertionError("unreachable")


def main() -> None:
    env = clean_env()
    # Built inside the repo's artifacts tree rather than the system temp: on macOS /var is a symlink
    # to /private/var, and MSBuild's relative ProjectReference resolution counts the symlink's
    # segments, so a fixture under $TMPDIR cannot find the project it references.
    work = ROOT / "artifacts" / "obj" / "Qyl.AddQylBehavior"
    shutil.rmtree(work, ignore_errors=True)
    work.mkdir(parents=True, exist_ok=True)
    try:
        (work / "Program.cs").write_text(PROGRAM, encoding="utf-8")
        project = work / "Qyl.AddQylBehavior.csproj"
        project.write_text(
            PROJECT.format(tfm=TARGET_FRAMEWORK, hosting=HOSTING_PROJECT),
            encoding="utf-8",
        )
        # The repo's Directory.Build.props/.Packages.props do not reach a temp directory, so the
        # fixture is built standalone against the hosting project reference alone.
        (work / "Directory.Build.props").write_text("<Project />", encoding="utf-8")
        (work / "Directory.Packages.props").write_text("<Project />", encoding="utf-8")

        run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], work, env)
        completed = subprocess.run(
            ["dotnet", "run", "--project", str(project), "-c", "Release", "--no-build"],
            cwd=work,
            env=env,
            text=True,
            stdout=subprocess.PIPE,
            stderr=subprocess.PIPE,
            check=False,
        )
    finally:
        shutil.rmtree(work, ignore_errors=True)

    if completed.returncode != 0:
        fail(
            "AddQyl behaviour fixture failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )

    report = parse_report(completed.stdout)

    elapsed = float(report["AddQylMilliseconds"])
    if elapsed >= ADDQYL_BUDGET_MILLISECONDS:
        fail(
            f"AddQyl blocked for {elapsed} ms with nothing listening; the collector probe must run "
            f"off the caller's thread (budget {ADDQYL_BUDGET_MILLISECONDS} ms)"
        )

    exports = int(report["ExportRequests"])
    if exports != 1:
        fail(
            f"one span produced {exports} OTLP export requests; two AddQyl calls must build one "
            "exporter and one of each processor"
        )

    build_time_exports = int(report["BuildTimeExportRequests"])
    if build_time_exports != 0:
        fail(
            f"the build-time document host sent {build_time_exports} OTLP export requests; a host "
            "that only builds the OpenAPI document must reach no collector, whatever "
            "OTEL_EXPORTER_OTLP_ENDPOINT says"
        )

    print(f"  - AddQyl returned in {elapsed} ms with no collector listening")
    print(f"  - two AddQyl calls, one span, {exports} OTLP export request")
    print(f"  - GetDocument.Insider exported {build_time_exports} times with the endpoint set")
    print("addqyl-behavior-ok")


if __name__ == "__main__":
    main()
