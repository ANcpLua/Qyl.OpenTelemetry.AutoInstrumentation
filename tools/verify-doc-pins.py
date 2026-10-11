#!/usr/bin/env python3
"""Fail when a README or contract YAML names a version that Directory.Packages.props does not pin.

Directory.Packages.props is the one source of package versions. The READMEs and the hand-written
contract YAMLs repeat those versions for readers, and every copy drifted once already. This gate
compares each copy against its source:

- a package id followed by a version, and each "Pinned" table row, against Directory.Packages.props;
- a backticked NuGet range, against the MassTransit.RabbitMQ range;
- the live-check registry tag, against SEMCONV_REF in live-check.yml;
- the N-row contract count, against the total in contracts/coverage-matrix.md;
- a QylGeneratedCodeAbi.V<N> anchor, against the Directory.Build.props major;
- a nuget.org/packages/<id>/<version> link in a contract YAML, against Directory.Packages.props.

The "verified against" strings in the contract YAMLs are derived from the same pins by
tools/generate-contract-artifacts.py, which fails when the YAML differs.
"""
from __future__ import annotations

import re
import sys
import xml.etree.ElementTree as ElementTree
from pathlib import Path
from typing import NoReturn

ROOT = Path(__file__).resolve().parents[1]
PACKAGES_PROPS = ROOT / "Directory.Packages.props"
BUILD_PROPS = ROOT / "Directory.Build.props"
LIVE_CHECK = ROOT / ".github" / "workflows" / "live-check.yml"
COVERAGE_MATRIX = ROOT / "contracts" / "coverage-matrix.md"
CONTRACT_YAMLS = [
    ROOT / "contracts" / "qyl-native-instrumentations.yaml",
    ROOT / "contracts" / "qyl-aot-ownership.yaml",
]

# Table rows whose first cell names the library rather than the package id.
ROW_PACKAGES = {
    "MySql.Data": ["MySql.Data"],
    "Elastic.Transport, Elastic.Clients.Elasticsearch": ["Elastic.Transport", "Elastic.Clients.Elasticsearch"],
    "GraphQL.NET": ["GraphQL"],
    "Grpc.Net.Client": ["Grpc.Net.Client"],
    "MassTransit": ["MassTransit.RabbitMQ"],
    "MongoDB.Driver": ["MongoDB.Driver"],
    "MySqlConnector": ["MySqlConnector"],
    "Npgsql": ["Npgsql"],
    "NServiceBus": ["NServiceBus"],
    "Oracle.ManagedDataAccess.Core": ["Oracle.ManagedDataAccess.Core"],
    "Quartz.NET": ["Quartz"],
    "RabbitMQ.Client": ["RabbitMQ.Client"],
    "Confluent.Kafka": ["Confluent.Kafka"],
    "StackExchange.Redis": ["StackExchange.Redis"],
}

VERSION = r"\d+\.\d+\.\d+(?:-[0-9A-Za-z.\-]+)?"


def fail(message: str) -> NoReturn:
    raise SystemExit(f"doc-pins: {message}")


def package_pins() -> dict[str, str]:
    return {
        element.get("Include", ""): element.get("Version", "")
        for element in ElementTree.parse(PACKAGES_PROPS).getroot().iter("PackageVersion")
    }


def readmes() -> list[Path]:
    return [ROOT / "README.md", *sorted((ROOT / "src").glob("*/README.md"))]


def check_package_mentions(path: Path, text: str, pins: dict[str, str], errors: list[str]) -> None:
    for package_id, version in pins.items():
        pattern = re.compile(r"(?<![\w.])`?" + re.escape(package_id) + r"(?![\w.])`?\s+`?(" + VERSION + r")`?")
        for match in pattern.finditer(text):
            if match.group(1) != version:
                line = text.count("\n", 0, match.start()) + 1
                errors.append(f"{path.relative_to(ROOT)}:{line}: {package_id} {match.group(1)} != pin {version}")


def check_pinned_rows(path: Path, text: str, pins: dict[str, str], errors: list[str]) -> None:
    in_pinned_table = False
    for number, line in enumerate(text.splitlines(), start=1):
        if not line.startswith("| "):
            in_pinned_table = False
            continue
        cells = [cell.strip() for cell in line.strip().strip("|").split("|")]
        if len(cells) >= 2 and cells[1] == "Pinned":
            in_pinned_table = True
            continue
        if not in_pinned_table or cells[0] not in ROW_PACKAGES:
            continue
        cited = re.findall(r"`([^`]+)`", cells[1])
        expected = [pins[package_id] for package_id in ROW_PACKAGES[cells[0]]]
        if cited != expected:
            errors.append(f"{path.relative_to(ROOT)}:{number}: {cells[0]} pinned {cited} != {expected}")


def check_ranges(path: Path, text: str, pins: dict[str, str], errors: list[str]) -> None:
    expected = pins["MassTransit.RabbitMQ"]
    for match in re.finditer(r"`(\[\d[^`\],]*,[^`\]]*[\])])`", text):
        if match.group(1) != expected:
            line = text.count("\n", 0, match.start()) + 1
            errors.append(f"{path.relative_to(ROOT)}:{line}: range {match.group(1)} != MassTransit.RabbitMQ pin {expected}")


def check_registry_tag(path: Path, text: str, errors: list[str]) -> None:
    ref = re.search(r"^\s*SEMCONV_REF:\s*(\S+)\s*$", LIVE_CHECK.read_text(encoding="utf-8"), re.MULTILINE)
    if ref is None:
        fail(f"{LIVE_CHECK.relative_to(ROOT)} sets no SEMCONV_REF")
    for match in re.finditer(r"`(v" + VERSION + r")` registry", text):
        if match.group(1) != ref.group(1):
            errors.append(f"{path.relative_to(ROOT)}: registry {match.group(1)} != SEMCONV_REF {ref.group(1)}")


def check_row_count(path: Path, text: str, errors: list[str]) -> None:
    total = re.search(r"^\| Total contract items \| (\d+) \|$", COVERAGE_MATRIX.read_text(encoding="utf-8"), re.MULTILINE)
    if total is None:
        fail(f"{COVERAGE_MATRIX.relative_to(ROOT)} has no 'Total contract items' row")
    for match in re.finditer(r"(\d+)-row contract", text):
        if match.group(1) != total.group(1):
            errors.append(f"{path.relative_to(ROOT)}: {match.group(1)}-row contract != coverage matrix total {total.group(1)}")


def check_abi_anchor(path: Path, text: str, errors: list[str]) -> None:
    version = re.search(r"<Version>(\d+)\.", BUILD_PROPS.read_text(encoding="utf-8"))
    if version is None:
        fail(f"{BUILD_PROPS.relative_to(ROOT)} has no <Version>")
    for match in re.finditer(r"QylGeneratedCodeAbi\.V(\d+)", text):
        if match.group(1) != version.group(1):
            errors.append(f"{path.relative_to(ROOT)}: QylGeneratedCodeAbi.V{match.group(1)} != major {version.group(1)}")


def check_contract_links(path: Path, text: str, pins: dict[str, str], errors: list[str]) -> None:
    lowered = {package_id.lower(): (package_id, version) for package_id, version in pins.items()}
    for match in re.finditer(r"nuget\.org/packages/([\w.]+)/(" + VERSION + r")", text):
        entry = lowered.get(match.group(1).lower())
        if entry is not None and match.group(2) != entry[1]:
            line = text.count("\n", 0, match.start()) + 1
            errors.append(f"{path.relative_to(ROOT)}:{line}: {entry[0]} link {match.group(2)} != pin {entry[1]}")
    check_package_mentions(path, text, pins, errors)


def main() -> None:
    pins = package_pins()
    errors: list[str] = []
    for path in readmes():
        text = path.read_text(encoding="utf-8")
        check_package_mentions(path, text, pins, errors)
        check_pinned_rows(path, text, pins, errors)
        check_ranges(path, text, pins, errors)
        check_registry_tag(path, text, errors)
        check_row_count(path, text, errors)
        check_abi_anchor(path, text, errors)
    for path in CONTRACT_YAMLS:
        check_contract_links(path, path.read_text(encoding="utf-8"), pins, errors)
    if errors:
        print("\n".join(errors), file=sys.stderr)
        fail(f"{len(errors)} copied version(s) differ from their source")
    print("doc-pins-ok")


if __name__ == "__main__":
    main()
