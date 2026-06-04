#!/usr/bin/env python3
from __future__ import annotations

import os
import subprocess
import tempfile
import zipfile
from pathlib import Path

try:
    import fcntl
except ImportError:
    fcntl = None


ROOT = Path(__file__).resolve().parents[1]
PACK_LOCK_PATH = Path(tempfile.gettempdir()) / "qyl-dotnet-autoinstrumentation-pack.lock"
PROPS_PATH = ROOT / "Directory.Build.props"
CORE_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"


REQUIRED_PACKAGE_ENTRIES = {
    "analyzers/dotnet/cs/Qyl.AutoInstrumentation.SourceGenerators.dll",
    "buildTransitive/Qyl.AutoInstrumentation.targets",
    "buildTransitive/Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs",
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


def verify_targets(text: str) -> None:
    for token in [
        "<InterceptorsNamespaces>$(InterceptorsNamespaces);Qyl.AutoInstrumentation.Generated</InterceptorsNamespaces>",
        "<InterceptorsPreviewNamespaces>$(InterceptorsPreviewNamespaces);Qyl.AutoInstrumentation.Generated</InterceptorsPreviewNamespaces>",
        "Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs",
    ]:
        if token not in text:
            fail(f"buildTransitive targets missing token: {token}")


def verify_intercepts_attribute(text: str) -> None:
    for token in [
        "namespace System.Runtime.CompilerServices",
        "internal sealed class InterceptsLocationAttribute",
        "public InterceptsLocationAttribute(int version, string data)",
    ]:
        if token not in text:
            fail(f"InterceptsLocation attribute source missing token: {token}")


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

        targets = archive.read("buildTransitive/Qyl.AutoInstrumentation.targets").decode("utf-8")
        attribute = archive.read("buildTransitive/Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs").decode("utf-8")
        nuspec_name = next((name for name in names if name.endswith(".nuspec")), None)
        if nuspec_name is None:
            fail("package nuspec missing")

        nuspec = archive.read(nuspec_name).decode("utf-8")
        verify_targets(targets)
        verify_intercepts_attribute(attribute)

        for name, text in [
            ("targets", targets),
            ("InterceptsLocationAttribute", attribute),
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
