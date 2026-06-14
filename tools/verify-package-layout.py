#!/usr/bin/env python3
from __future__ import annotations

import subprocess
import tempfile
import zipfile
from pathlib import Path

from verify_helpers import clean_env, read_version, run_checked

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"


REQUIRED_PACKAGE_ENTRIES = {
    "analyzers/dotnet/cs/Qyl.AutoInstrumentation.SourceGenerators.dll",
    "build/Qyl.AutoInstrumentation.targets",
    "buildTransitive/Qyl.AutoInstrumentation.targets",
}

FORBIDDEN_PACKAGE_ENTRY_TOKENS = [
    "profiler",
    "startuphook",
    "startup-hook",
    "ilrewrite",
    "il-rewrite",
    "native/",
    "runtimes/",
]

FORBIDDEN_CONTENT_TOKENS = [
    "CORECLR_PROFILER",
    "DOTNET_STARTUP_HOOKS",
    "ICorProfiler",
    "ReJIT",
    "ILRewrite",
    "ILRewriter",
    "Assembly.Load",
    "System.Reflection",
    "Activator.CreateInstance",
    "dynamic ",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def pack_runtime(feed: Path, env: dict[str, str]) -> Path:
    feed.mkdir(parents=True)
    with PACK_LOCK_PATH.open("w", encoding="utf-8") as lock:
        if fcntl is not None:
            fcntl.flock(lock, fcntl.LOCK_EX)
        try:
            run_checked(
                ["dotnet", "pack", str(CORE_PROJECT), "-c", "Release", "-o", str(feed), "-v", "quiet"],
                ROOT,
                env,
            )
        finally:
            if fcntl is not None:
                fcntl.flock(lock, fcntl.LOCK_UN)

    version = read_version()
    package = feed / f"Qyl.AutoInstrumentation.{version}.nupkg"
    if not package.exists():
        fail(f"package missing: {package}")

    return package


def verify_targets(name: str, text: str) -> None:
    for token in [
        "QylAutoInstrumentationCoreBuildAssetsImported",
        "_QylAutoInstrumentationCoreBuildAssetsAlreadyImported",
        "<InterceptorsNamespaces>$(InterceptorsNamespaces);Qyl.AutoInstrumentation.Generated</InterceptorsNamespaces>",
    ]:
        if token not in text:
            fail(f"{name} missing token: {token}")

    for token in [
        "InterceptorsPreviewNamespaces",
        "Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs",
        "QylAutoInstrumentationDisableInterceptorsAttribute",
    ]:
        if token in text:
            fail(f"{name} contains obsolete package target token: {token}")


def verify_package(package: Path) -> None:
    with zipfile.ZipFile(package) as archive:
        names = set(archive.namelist())
        missing = REQUIRED_PACKAGE_ENTRIES - names
        if missing:
            fail(f"package missing required entries: {sorted(missing)}")

        for name in sorted(names):
            lowered = name.lower()
            for token in FORBIDDEN_PACKAGE_ENTRY_TOKENS:
                if token in lowered:
                    fail(f"package contains forbidden mechanism entry token {token}: {name}")
            if name.endswith("InterceptsLocationAttribute.g.cs"):
                fail(f"package must not ship a compilation-wide InterceptsLocationAttribute source: {name}")

        build_targets = archive.read("build/Qyl.AutoInstrumentation.targets").decode("utf-8")
        transitive_targets = archive.read("buildTransitive/Qyl.AutoInstrumentation.targets").decode("utf-8")
        nuspec_name = next((name for name in names if name.endswith(".nuspec")), None)
        if nuspec_name is None:
            fail("package nuspec missing")

        nuspec = archive.read(nuspec_name).decode("utf-8")
        if build_targets != transitive_targets:
            fail("build and buildTransitive targets diverged")

        verify_targets("build targets", build_targets)
        verify_targets("buildTransitive targets", transitive_targets)

        for name, text in [
            ("build targets", build_targets),
            ("buildTransitive targets", transitive_targets),
            ("nuspec", nuspec),
        ]:
            for token in FORBIDDEN_CONTENT_TOKENS:
                if token in text:
                    fail(f"{name} contains forbidden mechanism token: {token}")


def main() -> None:
    env = clean_env()
    with tempfile.TemporaryDirectory(prefix="qyl-package-layout-") as temp:
        package = pack_runtime(Path(temp) / "feed", env)
        verify_package(package)

    print("package-layout-ok")


if __name__ == "__main__":
    main()
