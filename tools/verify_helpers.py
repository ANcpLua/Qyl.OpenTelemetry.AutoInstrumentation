from __future__ import annotations

import importlib.util
import json
import os
import platform
import shutil
import socket
import subprocess
from pathlib import Path
from types import ModuleType
from typing import Any, NoReturn


ROOT = Path(__file__).resolve().parents[1]
PROPS_PATH = ROOT / "Directory.Build.props"
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"


def fail(message: str) -> NoReturn:
    raise SystemExit(message)


def remove_publish_outputs() -> str:
    """Delete artifacts/publish and return a one-line summary of what was freed.

    The AOT demos must publish into the repo's standard artifacts/ layout (redirecting
    via --artifacts-path breaks the prebuilt-Analyzer path contract — see
    verify-aot-publish-gate.py publish()), so every demo run regrows ~50-150 MB there
    and a full matrix leaves multiple GB behind. The publish tree is a pure verification
    byproduct — each verifier executes the binary and asserts within the same run — so
    successful gates drop it; failed gates keep it for inspection.
    """
    publish_root = ROOT / "artifacts" / "publish"
    if not publish_root.exists():
        return "artifacts/publish absent — nothing to remove"

    try:
        size_bytes = sum(f.stat().st_size for f in publish_root.rglob("*") if f.is_file())
        shutil.rmtree(publish_root)
    except OSError as error:
        # Best-effort hygiene: a locked file (AV scanner, IDE indexer, Windows handle)
        # must not flip a passing gate into a false failure. Leave the remainder for
        # the next run to sweep.
        return f"artifacts/publish cleanup skipped: {error}"

    return f"removed artifacts/publish ({size_bytes / (1024 * 1024):.0f} MB)"


# The one qyl variable a demo may inherit: the live-check gate sets it to its OTLP listener so
# the demo exports the spans it is asserting on. Every other OTEL_/QYL_ variable is stripped, so
# ambient configuration cannot change what a demo verifies.
LIVE_CHECK_ENDPOINT_VARIABLE = "QYL_LIVE_CHECK_ENDPOINT"


def clean_env() -> dict[str, str]:
    env = dict(os.environ)
    for key in list(env):
        if key == LIVE_CHECK_ENDPOINT_VARIABLE:
            continue
        if key.startswith("OTEL_") or key.startswith("QYL_"):
            del env[key]

    env["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    env["DOTNET_NOLOGO"] = "1"
    env["MSBUILDDISABLENODEREUSE"] = "1"
    return env


def read_version() -> str:
    text = PROPS_PATH.read_text(encoding="utf-8")
    prefix = "<Version>"
    suffix = "</Version>"
    start = text.find(prefix)
    if start < 0:
        raise SystemExit("Directory.Build.props is missing <Version>")

    end = text.find(suffix, start)
    if end < 0:
        raise SystemExit("Directory.Build.props has unterminated <Version>")

    return text[start + len(prefix) : end].strip()


def artifacts_bin_assembly(project: Path, assembly_name: str | None = None, configuration: str = "Release") -> Path:
    name = assembly_name or project.stem
    return ROOT / "artifacts" / "bin" / project.stem / configuration.lower() / f"{name}.dll"


def artifacts_publish_dir(project: Path, name: str, configuration: str = "Release") -> Path:
    return ROOT / "artifacts" / "publish" / project.stem / configuration.lower() / name


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
        raise SystemExit(
            "command failed: "
            + " ".join(command)
            + f"\nexit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )

    return completed


def run_managed(project: Path, env: dict[str, str]) -> subprocess.CompletedProcess[str]:
    run_checked(["dotnet", "build", str(project), "-c", "Release", "-v", "quiet"], ROOT, env)
    assembly = artifacts_bin_assembly(project)
    return subprocess.run(
        ["dotnet", str(assembly)],
        cwd=project.parent,
        env=env,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )


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


def runtime_identifier(gate: str) -> str:
    system = platform.system().lower()
    machine = platform.machine().lower()
    if system == "darwin":
        return "osx-arm64" if machine in {"arm64", "aarch64"} else "osx-x64"
    if system == "linux":
        return "linux-arm64" if machine in {"arm64", "aarch64"} else "linux-x64"
    if system == "windows":
        return "win-arm64" if machine in {"arm64", "aarch64"} else "win-x64"

    fail(f"unsupported {gate} platform: {platform.system()} {platform.machine()}")


def parse_report(name: str, stdout: str) -> dict[str, Any]:
    start = stdout.find("{\n")
    if start < 0:
        fail(f"{name} did not emit JSON report\nstdout={stdout}")

    try:
        report = json.loads(stdout[start:])
    except json.JSONDecodeError as exc:
        fail(f"{name} emitted invalid JSON report: {exc}\nstdout={stdout}")

    if not isinstance(report, dict):
        fail(f"{name} report must be a JSON object: {report!r}")
    return report


def find_free_port() -> int:
    with socket.socket(socket.AF_INET, socket.SOCK_STREAM) as probe:
        probe.bind(("127.0.0.1", 0))
        return int(probe.getsockname()[1])


def load_artifacts() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_contract_artifacts", ARTIFACTS_PATH)
    if spec is None or spec.loader is None:
        fail(f"cannot load contract artifact generator: {ARTIFACTS_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module
