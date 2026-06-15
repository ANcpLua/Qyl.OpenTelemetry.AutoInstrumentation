#!/usr/bin/env python3
from __future__ import annotations

import argparse
import json
import shutil
import subprocess
import sys
import textwrap
import threading
import time
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from pathlib import Path
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
WORK = Path("/tmp/qyl-otlp-collector-fixtures")
FEED = WORK / "feed"
APP = WORK / "collector-consumer"
VERIFIED = ROOT / "tools/Qyl.OpenTelemetry.AutoInstrumentation.OtlpCollectorFixtures/verified/httpclient-traces.collector.json"
EXPORTER_VERSION = "1.15.3"

REQUIRED_STRINGS = (
    "Qyl.OpenTelemetry.AutoInstrumentation",
    "qyl.instrumentation.domain",
    "http.client",
    "http.request.method",
    "GET",
    "http.response.status_code",
    "server.address",
    "downstream.example",
    "url.full",
    "https://downstream.example/probe?access_token=Redacted",
)

FORBIDDEN_STRINGS = (
    "url.path",
    "super-secret",
)


@dataclass(frozen=True)
class CapturedRequest:
    method: str
    path: str
    content_type: str
    body: bytes


class CollectorServer(ThreadingHTTPServer):
    requests: list[CapturedRequest]

    def __init__(self) -> None:
        super().__init__(("127.0.0.1", 0), CollectorHandler)
        self.requests = []


class CollectorHandler(BaseHTTPRequestHandler):
    server: CollectorServer

    def do_POST(self) -> None:
        length = int(self.headers.get("Content-Length", "0"))
        body = self.rfile.read(length)
        content_type = self.headers.get("Content-Type", "")
        self.server.requests.append(
            CapturedRequest(
                method="POST",
                path=self.path,
                content_type=normalize_content_type(content_type),
                body=body,
            )
        )
        self.send_response(200)
        self.send_header("Content-Type", "application/x-protobuf")
        self.end_headers()

    def log_message(self, _format: str, *_args: Any) -> None:
        return


def main() -> None:
    parser = argparse.ArgumentParser()
    parser.add_argument("--update-verified", action="store_true")
    args = parser.parse_args()

    clean_workdir()
    version = pack_local_packages()

    server = CollectorServer()
    thread = threading.Thread(target=server.serve_forever, daemon=True)
    thread.start()
    endpoint = f"http://127.0.0.1:{server.server_port}/v1/traces"

    try:
        write_consumer(version)
        run(["dotnet", "run", "-c", "Release", "--", endpoint], cwd=APP)
        report = build_report(server.requests)
    finally:
        server.shutdown()
        server.server_close()
        thread.join(timeout=5)

    if args.update_verified:
        VERIFIED.parent.mkdir(parents=True, exist_ok=True)
        VERIFIED.write_text(json.dumps(report, indent=2, sort_keys=True) + "\n")
        print(f"updated {VERIFIED.relative_to(ROOT)}")
        return

    expected = json.loads(VERIFIED.read_text())
    if report != expected:
        print("OTLP collector fixture mismatch", file=sys.stderr)
        print("expected:", json.dumps(expected, indent=2, sort_keys=True), file=sys.stderr)
        print("actual:", json.dumps(report, indent=2, sort_keys=True), file=sys.stderr)
        raise SystemExit(1)

    print("otlp-collector-fixtures-ok")


def clean_workdir() -> None:
    if WORK.exists():
        shutil.rmtree(WORK)
    FEED.mkdir(parents=True)
    APP.mkdir(parents=True)


def pack_local_packages() -> str:
    version = f"{read_package_version()}.otlpcollector.{time.time_ns()}"
    projects = (
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj",
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj",
    )
    for project in projects:
        if project.exists():
            run(
                [
                    "dotnet",
                    "pack",
                    str(project),
                    "-c",
                    "Release",
                    "-o",
                    str(FEED),
                    f"-p:Version={version}",
                    f"-p:PackageVersion={version}",
                ],
                cwd=ROOT,
            )

    package_prefix = "Qyl.OpenTelemetry.AutoInstrumentation."
    packages = sorted(FEED.glob(f"{package_prefix}*.nupkg"))
    runtime_packages = [package for package in packages if ".SourceGenerator." not in package.name]
    if not runtime_packages:
        raise SystemExit("Qyl.OpenTelemetry.AutoInstrumentation package was not produced")

    package = runtime_packages[-1]
    name = package.name
    if not name.startswith(package_prefix) or not name.endswith(".nupkg"):
        raise SystemExit(f"cannot infer package version from {name}")

    return name[len(package_prefix) : -len(".nupkg")]


def read_package_version() -> str:
    text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    prefix = "<Version>"
    suffix = "</Version>"
    start = text.find(prefix)
    if start < 0:
        raise SystemExit("Directory.Build.props is missing <Version>")

    start += len(prefix)
    end = text.find(suffix, start)
    if end < 0:
        raise SystemExit("Directory.Build.props has unterminated <Version>")

    version = text[start:end].strip()
    if not version:
        raise SystemExit("Directory.Build.props has empty <Version>")

    return version


def write_consumer(version: str) -> None:
    (APP / "NuGet.Config").write_text(
        textwrap.dedent(
            f"""
            <?xml version="1.0" encoding="utf-8"?>
            <configuration>
              <packageSources>
                <clear />
                <add key="qyl-local" value="{FEED}" />
                <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
              </packageSources>
            </configuration>
            """
        ).strip()
        + "\n"
    )
    (APP / "CollectorConsumer.csproj").write_text(
        textwrap.dedent(
            f"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation" Version="{version}" />
                <PackageReference Include="OpenTelemetry" Version="{EXPORTER_VERSION}" />
                <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="{EXPORTER_VERSION}" />
              </ItemGroup>
            </Project>
            """
        ).strip()
        + "\n"
    )
    (APP / "Program.cs").write_text(
        textwrap.dedent(
            """
            using System.Net;
            using OpenTelemetry;
            using OpenTelemetry.Exporter;
            using OpenTelemetry.Trace;
            using Qyl.OpenTelemetry.AutoInstrumentation;

            if (args.Length != 1)
                throw new InvalidOperationException("Expected OTLP trace endpoint.");

            using var provider = Sdk.CreateTracerProviderBuilder()
                .SetSampler(new AlwaysOnSampler())
                .AddSource(QylActivitySource.Name)
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(args[0]);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.TimeoutMilliseconds = 10_000;
                })
                .Build();

            using var http = new HttpClient(new StubHandler());

            using var response = await http.GetAsync("https://downstream.example/probe?access_token=super-secret");
            if (response.StatusCode != HttpStatusCode.NoContent)
                throw new InvalidOperationException("Unexpected stub response.");

            if (!provider.ForceFlush(10_000))
                throw new InvalidOperationException("OTLP trace export did not flush.");

            internal sealed class StubHandler : HttpMessageHandler
            {
                protected override Task<HttpResponseMessage> SendAsync(
                    HttpRequestMessage request,
                    CancellationToken cancellationToken)
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NoContent)
                    {
                        RequestMessage = request
                    });
                }
            }
            """
        ).strip()
        + "\n"
    )


def build_report(requests: list[CapturedRequest]) -> dict[str, Any]:
    non_empty = [request for request in requests if request.body]
    if len(non_empty) != 1:
        raise SystemExit(f"expected exactly one non-empty OTLP request, got {len(non_empty)}")

    request = non_empty[0]
    if request.method != "POST":
        raise SystemExit(f"expected POST, got {request.method}")
    if request.path != "/v1/traces":
        raise SystemExit(f"expected /v1/traces, got {request.path}")
    if request.content_type != "application/x-protobuf":
        raise SystemExit(f"expected application/x-protobuf, got {request.content_type}")

    strings = extract_protobuf_strings(request.body)
    missing = [value for value in REQUIRED_STRINGS if value not in strings]
    forbidden = [value for value in FORBIDDEN_STRINGS if any(value in candidate for candidate in strings)]
    if missing:
        raise SystemExit(f"OTLP payload is missing qyl contract strings: {missing}")
    if forbidden:
        raise SystemExit(f"OTLP payload leaked forbidden sensitive strings: {forbidden}")

    return {
        "wireFormat": "otlp-http-protobuf",
        "request": {
            "method": request.method,
            "path": request.path,
            "contentType": request.content_type,
        },
        "matchedStrings": sorted(REQUIRED_STRINGS),
        "forbiddenStrings": [],
    }


def extract_protobuf_strings(data: bytes) -> set[str]:
    strings: set[str] = set()
    parse_message(data, strings, 0)
    return strings


def parse_message(data: bytes, strings: set[str], depth: int) -> None:
    if depth > 16:
        return

    index = 0
    while index < len(data):
        try:
            tag, index = read_varint(data, index)
        except ValueError:
            return

        if tag == 0:
            return

        wire_type = tag & 0b111
        if wire_type == 0:
            try:
                _, index = read_varint(data, index)
            except ValueError:
                return
        elif wire_type == 1:
            index += 8
        elif wire_type == 2:
            try:
                length, index = read_varint(data, index)
            except ValueError:
                return

            end = index + length
            if end > len(data):
                return

            segment = data[index:end]
            index = end
            text = try_decode_text(segment)
            if text is not None:
                strings.add(text)
            if segment:
                parse_message(segment, strings, depth + 1)
        elif wire_type == 5:
            index += 4
        else:
            return


def read_varint(data: bytes, index: int) -> tuple[int, int]:
    shift = 0
    value = 0
    while index < len(data):
        byte = data[index]
        index += 1
        value |= (byte & 0x7F) << shift
        if byte < 0x80:
            return value, index
        shift += 7
        if shift > 63:
            break
    raise ValueError("invalid protobuf varint")


def try_decode_text(data: bytes) -> str | None:
    if not data or len(data) > 512:
        return None
    try:
        text = data.decode("utf-8")
    except UnicodeDecodeError:
        return None

    if not any(character.isalpha() for character in text):
        return None
    for character in text:
        ordinal = ord(character)
        if ordinal < 32 or ordinal == 127:
            return None
    return text


def normalize_content_type(content_type: str) -> str:
    return content_type.split(";", 1)[0].strip().lower()


def run(command: list[str], cwd: Path) -> None:
    completed = subprocess.run(command, cwd=cwd, check=False)
    if completed.returncode != 0:
        raise SystemExit(f"{' '.join(command)} failed with exit code {completed.returncode}")


if __name__ == "__main__":
    started = time.monotonic()
    try:
        main()
    finally:
        elapsed = time.monotonic() - started
        print(f"otlp-collector-fixtures-elapsed={elapsed:.1f}s")
