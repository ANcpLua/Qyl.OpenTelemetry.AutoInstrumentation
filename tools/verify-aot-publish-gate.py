#!/usr/bin/env python3
"""NativeAOT-publish gate: pins the AOT warning-cleanliness classification of the demo matrix.

Every demo NativeAOT-publishes to a working binary. The load-bearing, easy-to-silently-break
distinction this gate protects is *warning cleanliness* under the repo's strict
``TreatWarningsAsErrors``:

* ``CLEAN_DEMOS``          — publish **warning-clean**. qyl's own assemblies never emit trim/AOT
                             warnings, and neither do these demos' dependencies.
* ``VENDOR_WARNED_DEMOS``  — publish to a working native binary, but a **third-party** dependency
                             emits tolerated trim/AOT warnings (IL2104/IL3053/IL3002). These are the
                             libraries that are not themselves AOT/trim-annotated upstream; the real
                             ``verify-real-*-demo.py`` scripts publish them with
                             ``-p:TreatWarningsAsErrors=false`` and runtime-verify telemetry via
                             testcontainers. The value mapped to each demo is the warning source.

The gate fails on:

* **REGRESSION** — a ``CLEAN_DEMOS`` entry that no longer publishes warning-clean (e.g. qyl code
  started using reflection, or a dependency began emitting IL warnings). This is the primary signal.
* **HARD BREAK** — a ``VENDOR_WARNED_DEMOS`` entry that fails to AOT-publish even with warnings
  relaxed (a genuine error, not a tolerated warning).
* **PROMOTION**  — a ``VENDOR_WARNED_DEMOS`` entry that is now warning-clean (the upstream library
  shipped AOT annotations). Non-fatal by default (printed as an actionable "move it to CLEAN_DEMOS"
  notice); made fatal with ``--strict-promotion`` so the classification cannot drift silently.

Single source of truth for the classification lives in this file. Run locally:
    python3 tools/verify-aot-publish-gate.py --set clean          # fast regression gate
    python3 tools/verify-aot-publish-gate.py --set all            # + hard-break + promotion detection
    python3 tools/verify-aot-publish-gate.py --demo Qyl.RealHttpClientDemo
"""
from __future__ import annotations

import argparse
import os
import platform
import re
import subprocess
import sys
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
DEMOS_DIR = ROOT / "demos"

# Demos that NativeAOT-publish warning-clean under strict TreatWarningsAsErrors.
CLEAN_DEMOS: list[str] = [
    "Qyl.LiveInstrumentationDemo",
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
    "Qyl.RealTcgPublishingDemo",
]

# Demos that AOT-publish (working binary) but whose third-party dependency emits tolerated
# trim/AOT warnings. Value = the warning-source assembly(ies). Published with warnings relaxed.
VENDOR_WARNED_DEMOS: dict[str, str] = {
    "Qyl.RealAspNetCoreMetricsDemo": "Microsoft.AspNetCore.Components(.Endpoints)",
    "Qyl.RealEfCoreDemo": "Microsoft.EntityFrameworkCore(.Relational/.Sqlite)",
    "Qyl.RealGraphQlDemo": "GraphQL",
    "Qyl.RealKafkaDemo": "Confluent.Kafka",
    "Qyl.RealLog4NetDemo": "log4net, System.Configuration.ConfigurationManager",
    "Qyl.RealMassTransitDemo": "MassTransit(.Abstractions)",
    "Qyl.RealMongoDbDemo": "MongoDB.Driver, MongoDB.Bson",
    "Qyl.RealMySqlDataDemo": "MySql.Data, System.Configuration.ConfigurationManager",
    "Qyl.RealNServiceBusDemo": "NServiceBus.Core",
    "Qyl.RealOracleMdaDemo": "Oracle.ManagedDataAccess",
    "Qyl.RealQuartzDemo": "Quartz",
    "Qyl.RealSqlClientDemo": "Microsoft.Data.SqlClient, System.Configuration.ConfigurationManager",
    "Qyl.RealWcfClientDemo": "System.ServiceModel.Primitives, System.Reflection.DispatchProxy",
}

# Extra publish properties that mirror the per-demo verify-real-*-demo.py recipes.
EXTRA_PROPS: dict[str, list[str]] = {
    "Qyl.RealEfCoreDemo": ["-p:QylEfCoreUseCompiledModel=true"],
}

IL_LINE = re.compile(r"(?:warning|error)\s+IL\d{4}")
ASM_WARNED = re.compile(r"Assembly '([^']+)' produced (?:trim|AOT)")


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


def publish(demo: str, *, strict: bool, rid: str, env: dict[str, str]) -> tuple[int, list[str], list[str]]:
    project = DEMOS_DIR / demo / f"{demo}.csproj"
    if not project.exists():
        raise SystemExit(f"demo project not found: {project}")
    cmd = [
        "dotnet", "publish", str(project),
        "-c", "Release",
        "-r", rid,
        "-p:PublishAot=true",
        "--self-contained", "true",
        f"-p:TreatWarningsAsErrors={'true' if strict else 'false'}",
        "-clp:NoSummary",
        "-v", "minimal",
        *EXTRA_PROPS.get(demo, []),
    ]
    proc = subprocess.run(cmd, cwd=str(ROOT), env=env, text=True,
                          stdout=subprocess.PIPE, stderr=subprocess.STDOUT)
    lines = proc.stdout.splitlines()
    il = [ln.strip() for ln in lines if IL_LINE.search(ln)]
    asms = sorted({m.group(1) for ln in lines for m in [ASM_WARNED.search(ln)] if m})
    return proc.returncode, il, asms


def check_clean(demo: str, rid: str, env: dict[str, str]) -> tuple[str, str]:
    # Strict: any IL warning becomes an error, so exit 0 <=> warning-clean.
    code, il, _ = publish(demo, strict=True, rid=rid, env=env)
    if code == 0:
        return "ok", "warning-clean"
    return "regression", f"exit={code}; " + (il[0] if il else "no IL line captured (see log)")


def check_warned(demo: str, rid: str, env: dict[str, str]) -> tuple[str, str]:
    # Relaxed: must still produce a binary; vendor IL warnings are expected & tolerated.
    code, il, asms = publish(demo, strict=False, rid=rid, env=env)
    if code != 0:
        return "hard-break", f"exit={code} even with warnings relaxed; " + (il[0] if il else "see log")
    if not il:
        return "promotion", "now warning-clean upstream — move to CLEAN_DEMOS"
    return "ok", "vendor-warned: " + (", ".join(asms) if asms else VENDOR_WARNED_DEMOS.get(demo, "?"))


def main() -> None:
    parser = argparse.ArgumentParser(description=__doc__, formatter_class=argparse.RawDescriptionHelpFormatter)
    parser.add_argument("--set", choices=["clean", "warned", "all"], default="all")
    parser.add_argument("--demo", action="append", default=[], help="restrict to specific demo(s)")
    parser.add_argument("--rid", default=None, help="runtime identifier (default: host)")
    parser.add_argument("--strict-promotion", action="store_true",
                        help="treat a promotion (vendor demo gone warning-clean) as a gate failure")
    parser.add_argument("--list", action="store_true", help="print the classification and exit")
    args = parser.parse_args()

    if args.list:
        print(f"CLEAN_DEMOS ({len(CLEAN_DEMOS)}):")
        for d in CLEAN_DEMOS:
            print(f"  clean   {d}")
        print(f"VENDOR_WARNED_DEMOS ({len(VENDOR_WARNED_DEMOS)}):")
        for d, src in VENDOR_WARNED_DEMOS.items():
            print(f"  warned  {d}  <- {src}")
        return

    rid = args.rid or runtime_identifier()
    env = dict(os.environ)

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
    rows: list[tuple[str, str, str, str]] = []
    failures = 0
    promotions = 0
    for demo, kind in plan:
        verdict, detail = check_clean(demo, rid, env) if kind == "clean" else check_warned(demo, rid, env)
        icon = {"ok": "PASS", "regression": "FAIL", "hard-break": "FAIL", "promotion": "PROMO"}[verdict]
        print(f"  [{icon}] {kind:6} {demo}  {detail}", flush=True)
        rows.append((icon, kind, demo, detail))
        if verdict in ("regression", "hard-break"):
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
        raise SystemExit(f"aot-publish-gate FAILED: {failures} failure(s), {promotions} promotion(s)")
    print(f"aot-publish-gate-ok ({len(rows)} demos, {promotions} promotion notice(s))")


if __name__ == "__main__":
    main()
