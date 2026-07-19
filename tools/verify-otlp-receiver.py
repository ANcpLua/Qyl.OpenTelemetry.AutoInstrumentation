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

from google.protobuf.message import DecodeError
from opentelemetry.proto.collector.trace.v1.trace_service_pb2 import (
    ExportTraceServiceRequest,
    ExportTraceServiceResponse,
)
from opentelemetry.proto.trace.v1.trace_pb2 import Span


ROOT = Path(__file__).resolve().parents[1]
WORK = Path("/tmp/qyl-otlp-receiver-evidence")
FEED = WORK / "feed"
APP = WORK / "consumer"
VERIFIED = ROOT / "tools/Qyl.OpenTelemetry.AutoInstrumentation.OtlpReceiver/verified/trace-evidence.json"
OTEL_VERSION = "1.16.0"


@dataclass(frozen=True)
class OtlpRequest:
    method: str
    path: str
    content_type: str
    message: ExportTraceServiceRequest


@dataclass(frozen=True)
class HttpRequest:
    method: str
    path: str


class EvidenceServer(ThreadingHTTPServer):
    requests: list[Any]

    def __init__(self, handler: type[BaseHTTPRequestHandler]) -> None:
        super().__init__(("127.0.0.1", 0), handler)
        self.requests = []


class OtlpHandler(BaseHTTPRequestHandler):
    server: EvidenceServer

    def do_POST(self) -> None:
        body = self.rfile.read(int(self.headers.get("Content-Length", "0")))
        try:
            message = ExportTraceServiceRequest.FromString(body)
        except DecodeError as error:
            self.send_error(400, "invalid OTLP protobuf")
            raise RuntimeError("exporter sent invalid OTLP protobuf") from error

        self.server.requests.append(
            OtlpRequest(
                method="POST",
                path=self.path,
                content_type=normalize_content_type(self.headers.get("Content-Type", "")),
                message=message,
            )
        )
        response = ExportTraceServiceResponse().SerializeToString()
        self.send_response(200)
        self.send_header("Content-Type", "application/x-protobuf")
        self.send_header("Content-Length", str(len(response)))
        self.end_headers()
        self.wfile.write(response)

    def log_message(self, _format: str, *_args: Any) -> None:
        return


class DownstreamHandler(BaseHTTPRequestHandler):
    server: EvidenceServer

    def do_GET(self) -> None:
        self.server.requests.append(HttpRequest(method="GET", path=self.path))
        self.send_response(204)
        self.send_header("Content-Length", "0")
        self.end_headers()

    def log_message(self, _format: str, *_args: Any) -> None:
        return


def main() -> None:
    parser = argparse.ArgumentParser(description="Export a real request to a typed loopback OTLP receiver.")
    mode = parser.add_mutually_exclusive_group()
    mode.add_argument("--update-verified", action="store_true")
    mode.add_argument(
        "--published",
        metavar="VERSION",
        help="restore VERSION from nuget.org only instead of packing the local checkout",
    )
    args = parser.parse_args()

    clean_workdir()
    version = args.published or pack_local_packages()
    otlp = EvidenceServer(OtlpHandler)
    downstream = EvidenceServer(DownstreamHandler)
    threads = [
        threading.Thread(target=otlp.serve_forever, daemon=True),
        threading.Thread(target=downstream.serve_forever, daemon=True),
    ]
    for thread in threads:
        thread.start()

    try:
        write_consumer(version, published=args.published is not None)
        run(
            [
                "dotnet",
                "run",
                "-c",
                "Release",
                "--",
                f"http://127.0.0.1:{otlp.server_port}/v1/traces",
                f"http://127.0.0.1:{downstream.server_port}/probe?access_token=super-secret",
            ],
            cwd=APP,
        )
        report = build_report(otlp.requests, downstream.requests, version)
    finally:
        for server in (otlp, downstream):
            server.shutdown()
            server.server_close()
        for thread in threads:
            thread.join(timeout=5)

    rendered = json.dumps(report, indent=2, sort_keys=True) + "\n"
    if args.update_verified:
        VERIFIED.parent.mkdir(parents=True, exist_ok=True)
        VERIFIED.write_text(rendered, encoding="utf-8")
        print(f"updated {VERIFIED.relative_to(ROOT)}")
        return

    expected_report = json.loads(VERIFIED.read_text(encoding="utf-8"))
    expected_report["span"]["scope"]["version"] = version
    if report != expected_report:
        print("typed OTLP receiver evidence mismatch", file=sys.stderr)
        print("expected:", json.dumps(expected_report, indent=2, sort_keys=True), file=sys.stderr)
        print("actual:", rendered, file=sys.stderr)
        raise SystemExit(1)

    if args.published:
        print(f"otlp-receiver-published-ok version={version}")
    else:
        print("otlp-receiver-evidence-ok")


def clean_workdir() -> None:
    if WORK.exists():
        shutil.rmtree(WORK)
    FEED.mkdir(parents=True)
    APP.mkdir(parents=True)


def pack_local_packages() -> str:
    base = read_package_version()
    separator = "." if "-" in base else "-"
    version = f"{base}{separator}otlpreceiver"
    for project in (
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators/Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj",
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj",
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners.csproj",
        ROOT / "src/Qyl.OpenTelemetry.AutoInstrumentation.Hosting/Qyl.OpenTelemetry.AutoInstrumentation.Hosting.csproj",
    ):
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

    package = next(FEED.glob(f"Qyl.OpenTelemetry.AutoInstrumentation.{version}.nupkg"), None)
    if package is None:
        raise SystemExit("runtime package was not produced")
    return version


def read_package_version() -> str:
    text = (ROOT / "Directory.Build.props").read_text(encoding="utf-8")
    start = text.index("<Version>") + len("<Version>")
    return text[start:text.index("</Version>", start)].strip()


def write_consumer(version: str, *, published: bool) -> None:
    package_sources = [] if published else [f'<add key="qyl-local" value="{FEED}" />']
    package_sources.append('<add key="nuget.org" value="https://api.nuget.org/v3/index.json" />')
    (APP / "NuGet.Config").write_text(
        "\n".join(
            [
                '<?xml version="1.0" encoding="utf-8"?>',
                "<configuration>",
                "  <packageSources>",
                "    <clear />",
                *(f"    {source}" for source in package_sources),
                "  </packageSources>",
                "</configuration>",
                "",
            ]
        ),
        encoding="utf-8",
    )
    (APP / "Consumer.csproj").write_text(
        textwrap.dedent(
            f"""
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <OutputType>Exe</OutputType>
                <TargetFramework>net10.0</TargetFramework>
                <ImplicitUsings>enable</ImplicitUsings>
                <Nullable>enable</Nullable>
                <RestoreNoCache>true</RestoreNoCache>
                <RestorePackagesPath>{WORK / "packages"}</RestorePackagesPath>
              </PropertyGroup>
              <ItemGroup>
                <PackageReference Include="Qyl.OpenTelemetry.AutoInstrumentation.Hosting" Version="{version}" />
                <PackageReference Include="OpenTelemetry" Version="{OTEL_VERSION}" />
                <PackageReference Include="OpenTelemetry.Exporter.OpenTelemetryProtocol" Version="{OTEL_VERSION}" />
              </ItemGroup>
            </Project>
            """
        ).strip() + "\n",
        encoding="utf-8",
    )
    (APP / "Program.cs").write_text(
        textwrap.dedent(
            """
            using OpenTelemetry;
            using OpenTelemetry.Exporter;
            using OpenTelemetry.Trace;
            using Qyl.OpenTelemetry.AutoInstrumentation;

            if (args.Length != 2)
                throw new InvalidOperationException("Expected OTLP and downstream endpoints.");

            using var provider = Sdk.CreateTracerProviderBuilder()
                .SetSampler(new AlwaysOnSampler())
                .AddSource("Qyl.OpenTelemetry.AutoInstrumentation")
                .AddOtlpExporter(options =>
                {
                    options.Endpoint = new Uri(args[0]);
                    options.Protocol = OtlpExportProtocol.HttpProtobuf;
                    options.TimeoutMilliseconds = 10_000;
                })
                .Build();

            using var http = new HttpClient();
            using var response = await http.GetAsync(args[1]);
            if ((int)response.StatusCode != 204)
                throw new InvalidOperationException("Loopback downstream returned an unexpected status.");
            if (!provider.ForceFlush(10_000))
                throw new InvalidOperationException("OTLP trace export did not flush.");
            """
        ).strip() + "\n",
        encoding="utf-8",
    )


def build_report(
    otlp_requests: list[Any],
    downstream_requests: list[Any],
    version: str,
) -> dict[str, Any]:
    if downstream_requests != [HttpRequest("GET", "/probe?access_token=super-secret")]:
        raise SystemExit(f"unexpected downstream requests: {downstream_requests}")
    if len(otlp_requests) != 1 or not isinstance(otlp_requests[0], OtlpRequest):
        raise SystemExit(f"expected one typed OTLP request, got {otlp_requests}")

    request = otlp_requests[0]
    if (request.method, request.path, request.content_type) != (
        "POST",
        "/v1/traces",
        "application/x-protobuf",
    ):
        raise SystemExit(f"unexpected OTLP request metadata: {request}")

    candidates: list[tuple[Any, Span]] = []
    for resource_spans in request.message.resource_spans:
        for scope_spans in resource_spans.scope_spans:
            for span in scope_spans.spans:
                candidates.append((scope_spans.scope, span))

    matches = [
        (scope, span)
        for scope, span in candidates
        if attributes(span).get("qyl.instrumentation.domain") == "http.client"
    ]
    if len(matches) != 1:
        raise SystemExit(f"expected exactly one qyl HTTP client span, got {len(matches)}")
    scope, span = matches[0]
    values = attributes(span)
    required = {
        "qyl.instrumentation.domain": "http.client",
        "http.request.method": "GET",
        "http.response.status_code": 204,
        "server.address": "127.0.0.1",
    }
    for key, expected in required.items():
        if values.get(key) != expected:
            raise SystemExit(f"unexpected {key}: expected={expected!r} actual={values.get(key)!r}")
    url = values.get("url.full")
    if not isinstance(url, str) or "access_token=Redacted" not in url or "super-secret" in url:
        raise SystemExit(f"url.full was not safely redacted: {url!r}")
    normalized_url, url_port = normalize_loopback_url(url)
    server_port = values.get("server.port")
    if not isinstance(server_port, int) or server_port <= 0 or server_port != url_port:
        raise SystemExit(
            f"server.port did not match the real loopback URL: attribute={server_port!r} url={url!r}"
        )
    if any("super-secret" in str(value) for value in values.values()):
        raise SystemExit("OTLP attributes leaked the downstream secret")
    if len(span.trace_id) != 16 or len(span.span_id) != 8:
        raise SystemExit("OTLP span carries invalid trace/span identifiers")
    if span.start_time_unix_nano <= 0 or span.end_time_unix_nano < span.start_time_unix_nano:
        raise SystemExit("OTLP span carries invalid timestamps")
    if scope.name != "Qyl.OpenTelemetry.AutoInstrumentation" or scope.version != version:
        raise SystemExit(
            "OTLP instrumentation scope did not match the package under test: "
            f"name={scope.name!r} version={scope.version!r} expected_version={version!r}"
        )

    return {
        "downstream": {"method": "GET", "path": "/probe?access_token=super-secret"},
        "otlp": {
            "contentType": request.content_type,
            "method": request.method,
            "path": request.path,
            "wireType": "ExportTraceServiceRequest",
        },
        "span": {
            "attributes": {
                **required,
                "server.port": "<port>",
                "url.full": normalized_url,
            },
            "idBytes": {"span": len(span.span_id), "trace": len(span.trace_id)},
            "kind": Span.SpanKind.Name(span.kind),
            "name": span.name,
            "scope": {"name": scope.name, "version": scope.version},
            "timestampsValid": True,
        },
    }


def attributes(span: Span) -> dict[str, Any]:
    result: dict[str, Any] = {}
    for attribute in span.attributes:
        kind = attribute.value.WhichOneof("value")
        result[attribute.key] = getattr(attribute.value, kind) if kind else None
    return result


def normalize_loopback_url(value: str) -> tuple[str, int]:
    prefix = "http://127.0.0.1:"
    if not value.startswith(prefix):
        raise SystemExit(f"expected loopback url.full, got {value!r}")
    port_text, separator, suffix = value[len(prefix):].partition("/")
    if not separator or not port_text.isdigit():
        raise SystemExit(f"expected numeric loopback port in url.full, got {value!r}")
    port = int(port_text)
    if not 0 < port <= 65535:
        raise SystemExit(f"loopback port is outside the TCP range: {port}")
    return prefix + "<port>/" + suffix, port


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
        print(f"otlp-receiver-evidence-elapsed={time.monotonic() - started:.1f}s")
