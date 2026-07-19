#!/usr/bin/env python3
"""Version-truth gate.

The instrumentation-scope version is stamped onto every emitted span and metric and must match
the shipped package. This gate enforces a single source of truth:

  * Directory.Build.props <Version> (the release-version owner) must be >= the latest stable v* tag,
  * any version-pinned README package-reference example must equal that version,
  * the generated-code ABI anchor and emitted references must exactly match the package major,
  * QylInstrumentation.Version must stay DERIVED from the build (no hardcoded semver literal).

Any mismatch is a hard build failure.
"""
from __future__ import annotations

import re
import subprocess
from pathlib import Path

ROOT = Path(__file__).resolve().parents[1]
PROPS = ROOT / "Directory.Build.props"
README = ROOT / "README.md"
INSTRUMENTATION = (
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylInstrumentation.cs"
)
GENERATED_CODE_ABI = (
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylGeneratedCodeAbi.cs"
)
GENERATOR = (
    ROOT
    / "src"
    / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators"
    / "QylAutoInstrumentationGenerator.cs"
)
GENERATED_INTERCEPTOR_SNAPSHOT = (
    ROOT
    / "tests"
    / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.Snapshots"
    / "verified"
    / "QylAutoInstrumentation.Interceptors.g.verified.cs"
)

STABLE_TAG = re.compile(r"^v(\d+)\.(\d+)\.(\d+)$")


def fail(message: str) -> None:
    raise SystemExit(f"version-sync: {message}")


def semver(text: str) -> tuple[int, int, int]:
    match = re.match(r"^(\d+)\.(\d+)\.(\d+)", text.strip())
    if not match:
        fail(f"cannot parse a semver from {text!r}")
    return int(match[1]), int(match[2]), int(match[3])


def props_version() -> str:
    match = re.search(r"<Version>\s*([^<]+?)\s*</Version>", PROPS.read_text(encoding="utf-8"))
    if not match:
        fail("no <Version> found in Directory.Build.props")
    return match[1]


def latest_stable_tag() -> tuple[int, int, int] | None:
    try:
        out = subprocess.run(
            ["git", "-C", str(ROOT), "tag", "--list", "v*", "--sort=-v:refname"],
            capture_output=True,
            text=True,
            check=True,
        ).stdout
    except (OSError, subprocess.CalledProcessError):
        return None  # No git / no tags (fresh checkout): tag-floor check is skipped.
    for line in out.splitlines():
        m = STABLE_TAG.match(line.strip())
        if m:
            return int(m[1]), int(m[2]), int(m[3])
    return None


def check_props_version(version: str) -> None:
    tag = latest_stable_tag()
    if tag is None:
        print("  - no stable v* tag reachable; skipping tag-floor comparison")
        return
    if semver(version) < tag:
        tag_text = ".".join(map(str, tag))
        fail(
            f"Directory.Build.props <Version> ({version}) is BEHIND the latest release tag "
            f"v{tag_text}. Bump the version to at least {tag_text}."
        )
    print(f"  - props version {version} >= latest tag v{'.'.join(map(str, tag))}")


def check_readme(version: str) -> None:
    text = README.read_text(encoding="utf-8")
    refs = re.findall(
        r'Include="Qyl\.OpenTelemetry\.AutoInstrumentation[.\w]*"\s+Version="([^"]+)"', text
    )
    if not refs:
        print("  - README uses the version-agnostic package-install command")
        return
    mismatched = sorted({v for v in refs if v != version})
    if mismatched:
        fail(
            f"README package-reference example(s) {mismatched} != props version {version}. "
            f"Update the README install snippets to {version}."
        )
    print(f"  - {len(refs)} README example(s) all pinned to {version}")


def check_generated_code_abi(version: str) -> None:
    package_major = semver(version)[0]
    expected_anchor = f"V{package_major}"
    expected_declaration = [(expected_anchor, str(package_major))]
    abi_text = GENERATED_CODE_ABI.read_text(encoding="utf-8")
    declarations = re.findall(r"\bpublic const int (V\d+)\s*=\s*(\d+)\s*;", abi_text)
    if declarations != expected_declaration:
        fail(
            "generated-code ABI declaration must exactly match the package major: "
            f"expected={expected_declaration!r} actual={declarations!r}"
        )

    expected_reference = (
        "global::Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode."
        f"QylGeneratedCodeAbi.{expected_anchor}"
    )
    reference_pattern = re.compile(
        r"global::Qyl\.OpenTelemetry\.AutoInstrumentation\.GeneratedCode\."
        r"QylGeneratedCodeAbi\.V\d+"
    )
    for label, path in [
        ("source generator", GENERATOR),
        ("generated interceptor snapshot", GENERATED_INTERCEPTOR_SNAPSHOT),
    ]:
        references = set(reference_pattern.findall(path.read_text(encoding="utf-8")))
        if references != {expected_reference}:
            fail(
                f"{label} must reference exactly {expected_reference}: "
                f"actual={sorted(references)!r}"
            )

    print(f"  - package major {package_major} exactly matches QylGeneratedCodeAbi.{expected_anchor}")


def check_version_is_derived() -> None:
    text = INSTRUMENTATION.read_text(encoding="utf-8")
    literal = re.search(r'\bVersion\s*=\s*"\d', text)
    if literal:
        fail(
            "QylInstrumentation.Version is a hardcoded semver literal again. It must be baked from "
            "the build <Version> via the generated QylVersionInfo const, so the scope version cannot "
            "drift from the package."
        )
    if "QylVersionInfo.Version" not in text:
        fail(
            "QylInstrumentation.Version no longer references the build-generated QylVersionInfo const; "
            "the scope version would be uncontrolled. See the GenerateQylVersionInfo target."
        )
    print("  - QylInstrumentation.Version is build-derived from generated QylVersionInfo (not a literal)")


def main() -> None:
    version = props_version()
    print(f"version-sync: single source of truth = Directory.Build.props <Version> = {version}")
    check_props_version(version)
    check_readme(version)
    check_generated_code_abi(version)
    check_version_is_derived()
    print("version-sync: OK")


if __name__ == "__main__":
    main()
