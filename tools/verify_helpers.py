from __future__ import annotations

import os
import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROPS_PATH = ROOT / "Directory.Build.props"


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
