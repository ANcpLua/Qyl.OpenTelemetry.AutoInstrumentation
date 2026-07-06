from __future__ import annotations

import os
import shutil
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROPS_PATH = ROOT / "Directory.Build.props"


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

    size_bytes = sum(f.stat().st_size for f in publish_root.rglob("*") if f.is_file())
    shutil.rmtree(publish_root)
    return f"removed artifacts/publish ({size_bytes / (1024 * 1024):.0f} MB)"


def clean_env() -> dict[str, str]:
    env = dict(os.environ)
    for key in list(env):
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
