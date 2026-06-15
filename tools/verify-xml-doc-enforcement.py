#!/usr/bin/env python3
from __future__ import annotations

import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_GENERATOR_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.csproj"
RUNTIME_PROJECT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"


REQUIRED_TOKENS = [
    "<GenerateDocumentationFile>true</GenerateDocumentationFile>",
    "<WarningsAsErrors>$(WarningsAsErrors);CS1591</WarningsAsErrors>",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def verify_project_contract(project: Path, label: str) -> None:
    text = project.read_text(encoding="utf-8")
    for token in REQUIRED_TOKENS:
        if token not in text:
            fail(f"{label} XML-doc enforcement token missing: {token}")


def verify_project_build(project: Path, label: str) -> None:
    completed = subprocess.run(
        [
            "dotnet",
            "build",
            str(project),
            "-c",
            "Release",
            "-v",
            "quiet",
        ],
        cwd=ROOT,
        text=True,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        check=False,
    )
    if completed.returncode != 0:
        fail(
            f"{label} XML-doc enforcement build failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )


def main() -> None:
    verify_project_contract(SOURCE_GENERATOR_PROJECT, "source generator")
    verify_project_contract(RUNTIME_PROJECT, "runtime")
    verify_project_build(SOURCE_GENERATOR_PROJECT, "source generator")
    verify_project_build(RUNTIME_PROJECT, "runtime")
    print("xml-doc-enforcement-ok scope=source-generator,runtime")


if __name__ == "__main__":
    main()
