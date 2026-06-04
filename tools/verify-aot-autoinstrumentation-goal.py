#!/usr/bin/env python3
from __future__ import annotations

import subprocess
import sys
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]


COMMANDS: list[tuple[str, list[str]]] = [
    ("contract invariants", [sys.executable, "tools/verify-contract-invariants.py"]),
    ("release build", ["dotnet", "build", "Qyl.AutoInstrumentation.slnx", "-c", "Release"]),
    ("consumer behavior", [sys.executable, "tools/verify-consumer-behavior.py"]),
    ("nativeaot consumer golden", [sys.executable, "tools/verify-nativeaot-consumer-golden.py"]),
    ("diff whitespace", ["git", "diff", "--check"]),
]


def main() -> None:
    for name, command in COMMANDS:
        print(f"== {name} ==")
        completed = subprocess.run(command, cwd=ROOT, check=False)
        if completed.returncode != 0:
            raise SystemExit(f"{name} failed with exit code {completed.returncode}")

    print("aot-autoinstrumentation-goal-ok")


if __name__ == "__main__":
    main()
