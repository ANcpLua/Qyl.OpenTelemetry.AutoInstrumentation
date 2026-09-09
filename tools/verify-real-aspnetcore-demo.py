#!/usr/bin/env python3
"""Runs the ASP.NET Core demo and holds it to the contract's own conformance signal.

The server span is ASP.NET Core's own `Microsoft.AspNetCore.Hosting.HttpRequestIn` activity.
This gate asserts what the contract row `signals.traces.ASPNETCORE` declares — exactly one
SERVER span per request, from the framework's source, named after its route and carrying every
attribute of the `aspnetcore.server` conformance signal. The attribute list is read from
`contracts/qyl-aot-ownership.yaml` rather than retyped here, so adding one to the contract
without emitting it fails this gate.
"""
from __future__ import annotations

import json
import platform
import subprocess
from pathlib import Path
from typing import Any, NoReturn

import yaml

from verify_helpers import artifacts_bin_assembly, artifacts_publish_dir, clean_env, run_checked

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealAspNetCoreDemo" / "Qyl.RealAspNetCoreDemo.csproj"
OWNERSHIP_PATH = ROOT / "contracts" / "qyl-aot-ownership.yaml"
CONTRACT_KEY = "signals.traces.ASPNETCORE"
CONFORMANCE_SIGNAL = "aspnetcore.server"
ASPNETCORE_SOURCE = "Microsoft.AspNetCore"
METHOD_KEY = "http.request.method"
ROUTE_KEY = "http.route"
# The demo drives one 204 request, one 500 request, one 404 that resolves no endpoint and one
# request whose client disconnects before anything is sent (499).
EXPECTED_SERVER_SPANS = 4
TARGET_FRAMEWORK = "net10.0"


def fail(message: str) -> NoReturn:
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

    fail(f"unsupported NativeAOT ASP.NET Core gate platform: {platform.system()} {platform.machine()}")


def parse_report(stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"ASP.NET Core demo did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"ASP.NET Core demo emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"ASP.NET Core demo report must be a JSON object: {report!r}")
    return report


def verify_report(name: str, completed: subprocess.CompletedProcess[str], expected_runtime_mode: str) -> None:
    if completed.returncode != 0:
        fail(
            f"{name} failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )
    if completed.stderr:
        fail(f"{name} wrote stderr:\n{completed.stderr}")

    report = parse_report(completed.stdout)
    if report.get("RuntimeMode") != expected_runtime_mode:
        fail(f"{name} runtime mode mismatch: expected={expected_runtime_mode} actual={report.get('RuntimeMode')}")
    if report.get("Pass") is not True:
        fail(f"{name} report did not pass:\n{json.dumps(report, indent=2, sort_keys=True)}")

    activities = report.get("Activities")
    if not isinstance(activities, list) or len(activities) < EXPECTED_SERVER_SPANS:
        fail(f"{name} expected at least {EXPECTED_SERVER_SPANS} ASP.NET Core activities, got {activities!r}")

    verify_conformance(name, activities)


def required_attributes() -> list[str]:
    contract = yaml.safe_load(OWNERSHIP_PATH.read_text(encoding="utf-8"))
    for item in contract.get("ownership_items") or []:
        if not isinstance(item, dict) or item.get("key") != CONTRACT_KEY:
            continue
        for signal in item.get("conformance_signals") or []:
            if signal.get("name") == CONFORMANCE_SIGNAL:
                return list(signal["required_attributes"])

    fail(f"{OWNERSHIP_PATH} declares no {CONFORMANCE_SIGNAL} conformance signal on {CONTRACT_KEY}")


def verify_conformance(name: str, activities: list[Any]) -> None:
    """One SERVER span per request, ASP.NET Core's own, named after its route and complete."""
    server_spans = [
        activity for activity in activities
        if isinstance(activity, dict) and activity.get("Kind") == "Server"
    ]
    if len(server_spans) != EXPECTED_SERVER_SPANS:
        fail(
            f"{name} exported {len(server_spans)} server spans for {EXPECTED_SERVER_SPANS} requests — "
            "a second one means a qyl-made server span came back:\n"
            + json.dumps(server_spans, indent=2, sort_keys=True)
        )

    required = required_attributes()
    for span in server_spans:
        if span.get("Source") != ASPNETCORE_SOURCE:
            fail(f"{name} server span is not ASP.NET Core's own: source={span.get('Source')!r} name={span.get('Name')!r}")

        tags = span.get("Tags")
        if not isinstance(tags, dict):
            fail(f"{name} server span carries no tags: {span!r}")

        # http.route is the one conditionally required attribute of the signal: a request that
        # resolved no endpoint has no template to carry. Everything else holds either way, and the
        # name still has to come from qyl rather than from the framework's operation name.
        route = tags.get(ROUTE_KEY)
        expected = required if route else [key for key in required if key != ROUTE_KEY]
        missing = [key for key in expected if key not in tags]
        if missing:
            fail(f"{name} server span {span.get('Name')!r} is missing {missing}")

        expected_name = f"{tags[METHOD_KEY]} {route}" if route else tags[METHOD_KEY]
        if span.get("Name") != expected_name:
            fail(f"{name} server span is named {span.get('Name')!r}, not after its route ({expected_name!r})")


def build_managed(env: dict[str, str]) -> Path:
    run_checked(
        ["dotnet", "build", str(PROJECT), "-c", "Release", "-v", "quiet", "--no-incremental"],
        ROOT,
        env,
    )
    return artifacts_bin_assembly(PROJECT)


def run_managed(assembly: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=PROJECT.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def publish_nativeaot(env: dict[str, str]) -> Path:
    output = artifacts_publish_dir(PROJECT, "nativeaot")
    run_checked(
        [
            "dotnet",
            "publish",
            str(PROJECT),
            "-c",
            "Release",
            "-r",
            runtime_identifier(),
            "-p:PublishAot=true",
            "--self-contained",
            "true",
            "-o",
            str(output),
            "-v",
            "quiet",
        ],
        ROOT,
        env,
    )
    executable = output / ("Qyl.RealAspNetCoreDemo.exe" if platform.system().lower() == "windows" else "Qyl.RealAspNetCoreDemo")
    if not executable.exists():
        fail(f"NativeAOT ASP.NET Core executable missing: {executable}")

    return executable


def run_nativeaot(executable: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    return subprocess.run(
        [str(executable)],
        cwd=executable.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


def main() -> None:
    env = clean_env()
    optin_env = dict(env)
    optin_env["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_REQUEST_HEADERS"] = "X-Demo-Req"
    optin_env["OTEL_DOTNET_AUTO_TRACES_ASPNETCORE_INSTRUMENTATION_CAPTURE_RESPONSE_HEADERS"] = "X-Demo-Res"
    optin_env["OTEL_DOTNET_EXPERIMENTAL_ASPNETCORE_DISABLE_URL_QUERY_REDACTION"] = "true"
    optin_env["AOT_PUBLISH_GATE_SET"] = env.get("AOT_PUBLISH_GATE_SET", "warned")

    managed_assembly = build_managed(env)
    managed = run_managed(managed_assembly, env)
    managed_optin = run_managed(managed_assembly, optin_env)
    nativeaot_executable = publish_nativeaot(env)
    nativeaot = run_nativeaot(nativeaot_executable, env)
    nativeaot_optin = run_nativeaot(nativeaot_executable, optin_env)

    verify_report("managed ASP.NET Core demo", managed, "dynamic-code-supported")
    verify_report("managed ASP.NET Core demo (capture opt-in)", managed_optin, "dynamic-code-supported")
    verify_report("NativeAOT ASP.NET Core demo", nativeaot, "nativeaot")
    verify_report("NativeAOT ASP.NET Core demo (capture opt-in)", nativeaot_optin, "nativeaot")
    print("real-aspnetcore-demo-ok")


if __name__ == "__main__":
    main()
