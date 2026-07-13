#!/usr/bin/env python3
from __future__ import annotations

from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
PROPS = ROOT / "Directory.Build.props"
PACKAGES = ROOT / "Directory.Packages.props"

PUBLIC_API_PROJECTS = [
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient",
]

EXCLUDED_PROJECTS = [
    "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators",
    "Qyl.RealAspNetCoreDemo",
    "Qyl.RealEfCoreDemo",
    "Qyl.RealGrpcClientDemo",
    "Qyl.RealHttpClientDemo",
    "Qyl.RealSqlClientDemo",
]


def fail(message: str) -> None:
    raise SystemExit(message)


def verify_props() -> None:
    text = PROPS.read_text(encoding="utf-8")
    packages = PACKAGES.read_text(encoding="utf-8")
    required_tokens = [
        "Microsoft.CodeAnalysis.PublicApiAnalyzers",
        "QylEnablePublicApiAnalyzers",
    ]
    for token in required_tokens:
        if token not in text:
            fail(f"Directory.Build.props missing PublicAPI token: {token}")

    package_tokens = [
        '<PackageVersion Include="Microsoft.CodeAnalysis.PublicApiAnalyzers" Version="3.3.4" />',
        "<ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>",
    ]
    for token in package_tokens:
        if token not in packages:
            fail(f"Directory.Packages.props missing PublicAPI token: {token}")

    for project in EXCLUDED_PROJECTS:
        if project in text and project != "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators":
            fail(f"Directory.Build.props should not explicitly enable PublicAPI analyzers for {project}")


def verify_api_file(path: Path, require_entries: bool) -> None:
    if not path.exists():
        fail(f"missing PublicAPI file: {path}")

    lines = path.read_text(encoding="utf-8").splitlines()
    if not lines or lines[0] != "#nullable enable":
        fail(f"{path} must start with #nullable enable")

    entries = [line for line in lines[1:] if line.strip()]
    if require_entries and not entries:
        fail(f"{path} must contain shipped public API entries")


def main() -> None:
    verify_props()
    for project in PUBLIC_API_PROJECTS:
        verify_api_file(project / "PublicAPI.Shipped.txt", require_entries=True)
        verify_api_file(project / "PublicAPI.Unshipped.txt", require_entries=False)

    print("public-api-baseline-ok")


if __name__ == "__main__":
    main()
