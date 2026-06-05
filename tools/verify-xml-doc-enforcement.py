#!/usr/bin/env python3
from __future__ import annotations

import subprocess
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
SOURCE_GENERATOR_PROJECT = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "Qyl.AutoInstrumentation.SourceGenerators.csproj"


REQUIRED_TOKENS = [
    "<GenerateDocumentationFile>true</GenerateDocumentationFile>",
    "<WarningsAsErrors>$(WarningsAsErrors);CS1591</WarningsAsErrors>",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def verify_project_contract() -> None:
    text = SOURCE_GENERATOR_PROJECT.read_text(encoding="utf-8")
    for token in REQUIRED_TOKENS:
        if token not in text:
            fail(f"source generator XML-doc enforcement token missing: {token}")


def verify_source_generator_build() -> None:
    completed = subprocess.run(
        [
            "dotnet",
            "build",
            str(SOURCE_GENERATOR_PROJECT),
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
            "source generator XML-doc enforcement build failed\n"
            f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
        )


def main() -> None:
    verify_project_contract()
    verify_source_generator_build()
    print("xml-doc-enforcement-ok scope=source-generator")


if __name__ == "__main__":
    main()
