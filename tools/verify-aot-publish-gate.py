#!/usr/bin/env python3
"""NativeAOT publish gate for warning-clean demos and exact third-party exceptions.

The relaxed publish exists only here. Each tolerated diagnostic is pinned by ID, owning assembly,
resolved package version, marker, and count; every native binary is executed by its real verifier.
"""
from __future__ import annotations

import argparse
from collections import Counter
import json
import os
import platform
import re
import subprocess
from pathlib import Path

from verify_helpers import artifacts_publish_dir, remove_publish_outputs

ROOT = Path(__file__).resolve().parents[1]
DEMOS_DIR = ROOT / "demos"
GENERATOR_PROJECT = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators"
                     / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj")

CLEAN_DEMOS: list[str] = [
    "Qyl.RealAdoNetDemo",
    "Qyl.RealAspNetCoreDemo",
    "Qyl.RealAzureDemo",
    "Qyl.RealElasticTransportDemo",
    "Qyl.RealElasticsearchDemo",
    "Qyl.RealGrpcClientDemo",
    "Qyl.RealHttpClientDemo",
    "Qyl.RealILoggerDemo",
    "Qyl.RealMySqlConnectorDemo",
    "Qyl.RealNLogDemo",
    "Qyl.RealNetRuntimeMetricsDemo",
    "Qyl.RealNpgsqlDemo",
    "Qyl.RealRabbitMqDemo",
    "Qyl.RealRedisDemo",
    "Qyl.RealSqliteDemo",
]

Approval = tuple[str, str, str, str, str, int]


def approved(diagnostic: str, assembly: str, package: str, version: str,
             count: int = 1, marker: str | None = None) -> Approval:
    return diagnostic, assembly, package.lower(), version, marker or f"Assembly '{assembly}' produced", count


VENDOR_WARNED_DEMOS: dict[str, tuple[Approval, ...]] = {
    "Qyl.RealAspNetCoreMetricsDemo": (
        approved("IL3053", "Microsoft.AspNetCore.Components.Endpoints", "microsoft.aspnetcore.app.runtime.{rid}", "10.0.9"),
        approved("IL2104", "Microsoft.AspNetCore.Components", "microsoft.aspnetcore.app.runtime.{rid}", "10.0.9"),
        approved("IL3053", "Microsoft.AspNetCore.Components", "microsoft.aspnetcore.app.runtime.{rid}", "10.0.9"),
    ),
    "Qyl.RealEfCoreDemo": (
        approved("IL2104", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore", "10.0.9"),
        approved("IL3053", "Microsoft.EntityFrameworkCore", "Microsoft.EntityFrameworkCore", "10.0.9"),
        approved("IL2104", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Relational", "10.0.9"),
        approved("IL3053", "Microsoft.EntityFrameworkCore.Relational", "Microsoft.EntityFrameworkCore.Relational", "10.0.9"),
        approved("IL2104", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.9"),
        approved("IL3053", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.9"),
        approved("IL3002", "Microsoft.EntityFrameworkCore.Sqlite", "Microsoft.EntityFrameworkCore.Sqlite.Core", "10.0.9", 3, "Microsoft.EntityFrameworkCore.Infrastructure.SpatialiteLoader.FindExtension()"),
        approved("IL3002", "Microsoft.Extensions.DependencyModel", "Microsoft.Extensions.DependencyModel", "10.0.9", marker="Microsoft.Extensions.DependencyModel.DependencyContext..cctor()"),
    ),
    "Qyl.RealGraphQlDemo": (
        approved("IL2104", "GraphQL", "GraphQL", "8.8.4"),
        approved("IL3053", "GraphQL", "GraphQL", "8.8.4"),
    ),
    "Qyl.RealMassTransitDemo": (
        approved("IL2104", "MassTransit", "MassTransit", "8.5.10"),
        approved("IL3053", "MassTransit", "MassTransit", "8.5.10"),
        approved("IL2104", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.10"),
        approved("IL3053", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.10"),
        approved("IL3000", "MassTransit.Abstractions", "MassTransit.Abstractions", "8.5.10", marker="MassTransit.Metadata.BusHostInfo."),
    ),
    "Qyl.RealMySqlDataDemo": (
        approved("IL2104", "MySql.Data", "MySql.Data", "9.7.0"),
        approved("IL2104", "System.Configuration.ConfigurationManager", "System.Configuration.ConfigurationManager", "8.0.0"),
    ),
    "Qyl.RealNServiceBusDemo": (
        approved("IL2104", "NServiceBus.Core", "NServiceBus", "10.2.5"),
        approved("IL3053", "NServiceBus.Core", "NServiceBus", "10.2.5"),
        approved("IL3000", "NServiceBus.Core", "NServiceBus", "10.2.5", 2, "NServiceBus.FileVersionRetriever."),
    ),
    "Qyl.RealOracleMdaDemo": (
        approved("IL2104", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.200"),
        approved("IL3053", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.200"),
        approved("IL3000", "Oracle.ManagedDataAccess", "Oracle.ManagedDataAccess.Core", "23.26.200", 5, "Oracle"),
    ),
    "Qyl.RealSqlClientDemo": (
        approved("IL2104", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.0.1"),
        approved("IL3053", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.0.1"),
        approved("IL2104", "Microsoft.Data.SqlClient.Internal.Logging", "Microsoft.Data.SqlClient.Internal.Logging", "1.0.0"),
        approved("IL2104", "System.Configuration.ConfigurationManager", "System.Configuration.ConfigurationManager", "9.0.13"),
    ),
    "Qyl.RealWcfClientDemo": (
        approved("IL2104", "System.ServiceModel.Primitives", "System.ServiceModel.Primitives", "10.0.652802"),
        approved("IL3053", "System.ServiceModel.Primitives", "System.ServiceModel.Primitives", "10.0.652802"),
        approved("IL3053", "System.Reflection.DispatchProxy", "microsoft.netcore.app.runtime.nativeaot.{rid}", "10.0.9"),
        approved("IL3053", "System.Private.DataContractSerialization", "microsoft.netcore.app.runtime.nativeaot.{rid}", "10.0.9"),
    ),
}

EXTERNAL_WARNED_PROJECTS = {
    "WebApiAotDemo": VENDOR_WARNED_DEMOS["Qyl.RealEfCoreDemo"] + (
        approved("IL2104", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.0.1"),
        approved("IL3053", "Microsoft.Data.SqlClient", "Microsoft.Data.SqlClient", "7.0.1"),
        approved("IL2104", "Microsoft.Data.SqlClient.Internal.Logging", "Microsoft.Data.SqlClient.Internal.Logging", "1.0.0"),
        approved("IL2104", "System.Configuration.ConfigurationManager", "System.Configuration.ConfigurationManager", "9.0.13"),
    ),
}

# Extra publish properties that mirror the per-demo verify-real-*-demo.py recipes.
EXTRA_PROPS: dict[str, list[str]] = {
    "Qyl.RealEfCoreDemo": ["-p:QylEfCoreUseCompiledModel=true"],
}

DIAGNOSTIC_LINE = re.compile(r"\b(?:warning|error)\s+([A-Z]+\d+)\b", re.IGNORECASE)
PACKAGE_PATH = re.compile(r"/(?:\.nuget/packages|packages)/([^/]+)/([^/]+)/", re.IGNORECASE)


def runtime_identifier() -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    arm = machine in {"arm64", "aarch64"}
    if system == "darwin":
        return "osx-arm64" if arm else "osx-x64"
    if system == "linux":
        return "linux-arm64" if arm else "linux-x64"
    if system == "windows":
        return "win-arm64" if arm else "win-x64"
    raise SystemExit(f"unsupported platform for the AOT gate: {system}/{machine}")


ERROR_LINE = re.compile(r": error |\berror IL\d|\berror CS\d|\berror MSB\d|\berror NETSDK\d")


def publish(project: Path, output: Path, *, strict: bool, rid: str,
            env: dict[str, str], extra_props: list[str] | None = None) -> tuple[int, list[str], str]:
    common = ["-c", "Release", "-r", rid, "-p:PublishAot=true", "--disable-build-servers"]
    commands = [
        ["dotnet", "build", str(project), "--no-incremental", *common,
         "-p:SelfContained=true", "-p:TreatWarningsAsErrors=true", "-v", "quiet", *(extra_props or [])],
        ["dotnet", "publish", str(project), *common, "--self-contained", "true",
         f"-p:TreatWarningsAsErrors={'true' if strict else 'false'}", "-o", str(output),
         "-clp:NoSummary", "-v", "minimal", *(extra_props or [])],
    ]
    for command in commands:
        proc = subprocess.run(command, cwd=str(ROOT), env=env, text=True,
                              stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
        if proc.returncode:
            break
    lines = proc.stdout.splitlines()
    diagnostics = [ln.strip() for ln in lines if DIAGNOSTIC_LINE.search(ln)]
    err = [ln.strip() for ln in lines if ERROR_LINE.search(ln)]
    tail = err[-1] if err else next((ln.strip() for ln in reversed(lines) if ln.strip()), "(no output)")
    return proc.returncode, diagnostics, tail[:240]


def publish_demo(demo: str, *, strict: bool, rid: str,
                 env: dict[str, str]) -> tuple[int, list[str], str, Path]:
    project = DEMOS_DIR / demo / f"{demo}.csproj"
    if not project.exists():
        raise SystemExit(f"demo project not found: {project}")
    result = publish(project, artifacts_publish_dir(project, "nativeaot"), strict=strict, rid=rid,
                     env=env, extra_props=EXTRA_PROPS.get(demo))
    return *result, project


def resolved_packages(project: Path, diagnostics: list[str]) -> set[tuple[str, str]]:
    packages: set[tuple[str, str]] = set()
    candidates = [ROOT / "artifacts" / "obj" / project.stem / "project.assets.json",
                  project.parent / "obj" / "project.assets.json"]
    assets = next((candidate for candidate in candidates if candidate.exists()), None)
    if assets is not None:
        data = json.loads(assets.read_text(encoding="utf-8"))
        packages.update(
            (name.lower(), version)
            for library, metadata in data.get("libraries", {}).items()
            if metadata.get("type") == "package"
            for name, version in [library.rsplit("/", 1)]
        )
    for line in diagnostics:
        match = PACKAGE_PATH.search(line.replace("\\", "/"))
        if match:
            packages.add((match.group(1).lower(), match.group(2)))
    return packages


def validate_warning_policy(name: str, project: Path, diagnostics: list[str], rid: str) -> tuple[bool, str]:
    policy = VENDOR_WARNED_DEMOS.get(name) or EXTERNAL_WARNED_PROJECTS.get(name)
    if policy is None:
        return False, f"no vendor-warning policy for {name}"

    expected: Counter[tuple[str, str, str, str]] = Counter()
    for diagnostic, assembly, package, version, _, count in policy:
        expected[(diagnostic, assembly, package.format(rid=rid), version)] += count

    actual: Counter[tuple[str, str, str, str]] = Counter()
    resolved = resolved_packages(project, diagnostics)
    for line in diagnostics:
        match = DIAGNOSTIC_LINE.search(line)
        diagnostic = match.group(1).upper() if match else "UNKNOWN"
        if "Assembly 'Qyl.OpenTelemetry." in line or "/src/qyl.opentelemetry." in line.lower().replace("\\", "/"):
            return False, f"qyl-owned diagnostic is never allowed: {line}"
        matches = [entry for entry in policy if entry[0] == diagnostic and entry[4] in line]
        if len(matches) != 1:
            return False, f"unapproved or ambiguous diagnostic: {line}"
        _, assembly, package, version, _, _ = matches[0]
        package = package.format(rid=rid)
        path_match = PACKAGE_PATH.search(line.replace("\\", "/"))
        if path_match and (path_match.group(1).lower(), path_match.group(2)) != (package, version):
            return False, (f"package drift for {diagnostic}/{assembly}: "
                           f"{path_match.group(1)}@{path_match.group(2)} != {package}@{version}")
        actual[(diagnostic, assembly, package, version)] += 1

    missing_packages = sorted({(package, version) for _, _, package, version in expected} - resolved)
    if missing_packages:
        return False, f"approved package/version not resolved: {missing_packages}"
    if actual != expected:
        return False, f"warning policy drift: missing={dict(expected - actual)} extra={dict(actual - expected)}"
    assemblies = sorted({assembly for _, assembly, _, _ in actual})
    return True, f"{sum(actual.values())} approved diagnostic(s) from {', '.join(assemblies)}"


def prebuild_generator(env: dict[str, str]) -> None:
    """Build the source generator into the standard artifacts/ layout before any publish.

    Under PublishAot the demos consume the generator as a prebuilt Analyzer DLL from
    artifacts/bin/...SourceGenerators/release — a contract every other workflow satisfies
    incidentally via a prior full-solution build. On a fresh workspace the gate must satisfy
    it explicitly or every demo publish dies with CS0006.
    """
    proc = subprocess.run(
        ["dotnet", "build", str(GENERATOR_PROJECT), "-c", "Release",
         "--disable-build-servers", "-clp:NoSummary", "-v", "minimal"],
        cwd=str(ROOT), env=env, text=True,
        stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    if proc.returncode != 0:
        tail = "\n".join(proc.stdout.splitlines()[-15:])
        raise SystemExit(f"generator prebuild failed (exit={proc.returncode}):\n{tail}")
    print("  generator prebuilt into artifacts/ (analyzer contract for PublishAot demos)", flush=True)


def check_clean(demo: str, rid: str, env: dict[str, str]) -> tuple[str, str]:
    code, diagnostics, tail, _ = publish_demo(demo, strict=True, rid=rid, env=env)
    if code == 0 and not diagnostics:
        return "ok", "warning-clean"
    return "regression", f"exit={code}; " + (diagnostics[0] if diagnostics else tail)


def check_warned(demo: str, rid: str, env: dict[str, str]) -> tuple[str, str]:
    code, diagnostics, tail, project = publish_demo(demo, strict=False, rid=rid, env=env)
    if code != 0:
        return "hard-break", f"exit={code} even with warnings relaxed; " + (diagnostics[0] if diagnostics else tail)
    if not diagnostics:
        return "promotion", "now warning-clean upstream — move to CLEAN_DEMOS"
    valid, detail = validate_warning_policy(demo, project, diagnostics, rid)
    return ("ok" if valid else "policy-drift"), detail


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--set", choices=["clean", "warned", "all"], default="all")
    parser.add_argument("--demo", action="append", default=[], help="restrict to specific demo(s)")
    parser.add_argument("--rid", default=None, help="runtime identifier (default: host)")
    parser.add_argument("--strict-promotion", action="store_true",
                        help="treat a promotion (vendor demo gone warning-clean) as a gate failure")
    parser.add_argument("--list", action="store_true", help="print the classification and exit")
    parser.add_argument("--project", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--policy", choices=sorted(EXTERNAL_WARNED_PROJECTS), help=argparse.SUPPRESS)
    parser.add_argument("--output", type=Path, help=argparse.SUPPRESS)
    parser.add_argument("--keep-publish", action="store_true",
                        help="keep artifacts/publish after a passing gate (default: removed — "
                             "pure verification byproduct, multiple GB over the full matrix)")
    args = parser.parse_args()

    if args.list:
        print(f"CLEAN_DEMOS ({len(CLEAN_DEMOS)}):")
        for d in CLEAN_DEMOS:
            print(f"  clean   {d}")
        print(f"VENDOR_WARNED_DEMOS ({len(VENDOR_WARNED_DEMOS)}):")
        for demo, policy in VENDOR_WARNED_DEMOS.items():
            packages = ", ".join(sorted({f"{package}@{version}" for _, _, package, version, _, _ in policy}))
            print(f"  warned  {demo}  <- {packages}")
        return

    rid = args.rid or runtime_identifier()
    env = dict(os.environ)
    external = [args.project, args.policy, args.output]
    if any(external):
        if not all(external):
            raise SystemExit("--project, --policy, and --output must be supplied together")
        code, diagnostics, tail = publish(args.project, args.output, strict=False, rid=rid, env=env)
        if code != 0:
            raise SystemExit(f"external AOT publish failed: exit={code}; " + (diagnostics[0] if diagnostics else tail))
        if not diagnostics:
            if args.strict_promotion:
                raise SystemExit(f"{args.policy} is now warning-clean; remove its vendor boundary")
            print(f"{args.policy} promotion: now warning-clean")
            return
        valid, detail = validate_warning_policy(args.policy, args.project, diagnostics, rid)
        if not valid:
            raise SystemExit(f"{args.policy} warning policy failed: {detail}")
        print(f"external-aot-publish-gate-ok {args.policy}: {detail}")
        return

    plan: list[tuple[str, str]] = []  # (demo, kind)
    if args.set in ("clean", "all"):
        plan += [(d, "clean") for d in CLEAN_DEMOS]
    if args.set in ("warned", "all"):
        plan += [(d, "warned") for d in VENDOR_WARNED_DEMOS]
    if args.demo:
        wanted = set(args.demo)
        plan = [(d, k) for d, k in plan if d in wanted]
        if not plan:
            raise SystemExit(f"none of {sorted(wanted)} are in the gate classification")

    print(f"AOT-publish gate | rid={rid} | {len(plan)} demo(s) | set={args.set}", flush=True)
    prebuild_generator(env)
    rows: list[tuple[str, str, str, str]] = []
    failures = 0
    promotions = 0
    for demo, kind in plan:
        verdict, detail = (check_clean(demo, rid, env) if kind == "clean"
                           else check_warned(demo, rid, env))
        icon = {"ok": "PASS", "regression": "FAIL", "hard-break": "FAIL",
                "policy-drift": "FAIL", "promotion": "PROMO"}[verdict]
        print(f"  [{icon}] {kind:6} {demo}  {detail}", flush=True)
        rows.append((icon, kind, demo, detail))
        if verdict in ("regression", "hard-break", "policy-drift"):
            failures += 1
        elif verdict == "promotion":
            promotions += 1
            if args.strict_promotion:
                failures += 1

    summary = os.environ.get("GITHUB_STEP_SUMMARY")
    if summary:
        with open(summary, "a", encoding="utf-8") as fh:
            fh.write(f"### AOT-publish gate (`{rid}`)\n\n")
            fh.write("| result | set | demo | detail |\n|---|---|---|---|\n")
            for icon, kind, demo, detail in rows:
                fh.write(f"| {icon} | {kind} | `{demo}` | {detail} |\n")
            fh.write(f"\n**{failures} failure(s), {promotions} promotion(s)** over {len(rows)} demo(s).\n")

    if failures:
        # Keep artifacts/publish on failure so the offending binaries stay inspectable.
        raise SystemExit(f"aot-publish-gate FAILED: {failures} failure(s), {promotions} promotion(s)")
    if not args.keep_publish:
        print(remove_publish_outputs())
    print(f"aot-publish-gate-ok ({len(rows)} demos, {promotions} promotion notice(s))")


if __name__ == "__main__":
    main()
