#!/usr/bin/env python3
"""Version-truth gate.

The instrumentation-scope version is stamped onto every emitted span/metric and must never drift
from the shipped package again. This gate enforces a single source of truth:

  * Directory.Build.props <Version> (the release-version owner) must be >= the latest stable v* tag,
  * any version-pinned README package-reference example must equal that version,
  * QylInstrumentation.Version must stay DERIVED from the build (no hardcoded semver literal).

Historically all three drifted independently (const "0.3.0-pre.1", props 3.0.2, tag v3.1.2); this
gate makes that state a hard build failure.
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
    check_version_is_derived()
    print("version-sync: OK")


if __name__ == "__main__":
    main()
