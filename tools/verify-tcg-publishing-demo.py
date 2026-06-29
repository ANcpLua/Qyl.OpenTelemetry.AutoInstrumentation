#!/usr/bin/env python3
from __future__ import annotations

import subprocess
from pathlib import Path

from verify_helpers import clean_env

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "demos" / "Qyl.RealTcgPublishingDemo" / "Qyl.RealTcgPublishingDemo.csproj"


def fail(message: str) -> None:
    raise SystemExit(message)


def main() -> None:
    env = clean_env()
    completed = subprocess.run(
        ["dotnet", "run", "--project", str(PROJECT), "-c", "Release", "-v", "quiet"],
        cwd=ROOT,
        env=env,
        capture_output=True,
        text=True,
        check=False,
    )
    if completed.returncode != 0:
        fail(
            "tcg publishing demo failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )

    stdout = completed.stdout
    # The demo attaches an OTel log processor (where an OTLP exporter would sit) and asserts the
    # emitted LogRecord matches the binary's own TCG before printing the marker below.
    for required in (
        "event=qyl.telemetry_capability_graph",
        "body_is_tcg_json=True",
        "tcg-publishing-ok",
    ):
        if required not in stdout:
            fail(f"tcg publishing demo missing expected output: {required!r}\nstdout={stdout}")

    print("tcg-publishing-demo-ok")


if __name__ == "__main__":
    main()
