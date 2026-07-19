from __future__ import annotations

import argparse
import importlib.util
import json
import os
import re
import subprocess
import tempfile
from pathlib import Path
from types import ModuleType
from typing import Any, Iterable


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"
MANIFEST_PREFIX = "// qyl-interceptor-manifest: "
GENERATED_INTERCEPTOR_FILE = "QylAutoInstrumentation.Interceptors.g.cs"
GENERATOR_OUTPUT_SEGMENT = "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators"
MANIFEST_FIELDS = (
    "interceptorKind",
    "signal",
    "instrumentationId",
    "additionalMetricIds",
    "contractKeys",
)


class ManifestCoverageError(RuntimeError):
    pass


def fail(message: str) -> None:
    raise ManifestCoverageError(message)


def load_artifacts() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_contract_artifacts_for_manifests", ARTIFACTS_PATH)
    if spec is None or spec.loader is None:
        fail(f"cannot load contract artifact generator: {ARTIFACTS_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def strict_json_object(payload: str, context: str) -> dict[str, Any]:
    def reject_duplicate_keys(pairs: list[tuple[str, Any]]) -> dict[str, Any]:
        result: dict[str, Any] = {}
        for key, value in pairs:
            if key in result:
                raise ValueError(f"duplicate JSON key {key!r}")
            result[key] = value
        return result

    try:
        value = json.loads(payload, object_pairs_hook=reject_duplicate_keys)
    except (json.JSONDecodeError, ValueError) as error:
        fail(f"invalid interceptor manifest JSON in {context}: {error}")
    if not isinstance(value, dict):
        fail(f"interceptor manifest must be a JSON object in {context}")
    return value


def verify_manifest_shape(manifest: dict[str, Any], context: str) -> None:
    actual_fields = tuple(manifest)
    if actual_fields != MANIFEST_FIELDS:
        fail(
            f"interceptor manifest fields are not canonical in {context}: "
            f"expected={MANIFEST_FIELDS} actual={actual_fields}"
        )

    kind = manifest["interceptorKind"]
    signal = manifest["signal"]
    instrumentation_id = manifest["instrumentationId"]
    additional_metric_ids = manifest["additionalMetricIds"]
    contract_keys = manifest["contractKeys"]
    if not isinstance(kind, str) or not kind:
        fail(f"interceptor manifest kind must be a non-empty string in {context}")
    if signal not in {"traces", "metrics", "logs"}:
        fail(f"interceptor manifest signal is invalid in {context}: {signal!r}")
    if not isinstance(instrumentation_id, str) or re.fullmatch(r"[A-Z0-9]+", instrumentation_id) is None:
        fail(f"interceptor manifest instrumentation id is invalid in {context}: {instrumentation_id!r}")
    if not isinstance(additional_metric_ids, list) or any(
        not isinstance(item, str) or re.fullmatch(r"[A-Z0-9]+", item) is None
        for item in additional_metric_ids
    ):
        fail(f"interceptor manifest additional metric ids are invalid in {context}")
    if len(set(additional_metric_ids)) != len(additional_metric_ids):
        fail(f"interceptor manifest additional metric ids contain duplicates in {context}")
    if not isinstance(contract_keys, list) or any(not isinstance(item, str) for item in contract_keys):
        fail(f"interceptor manifest contract keys are invalid in {context}")

    expected_contract_keys = [f"signals.{signal}.{instrumentation_id}"]
    expected_contract_keys.extend(f"signals.metrics.{item}" for item in additional_metric_ids)
    if contract_keys != expected_contract_keys:
        fail(
            f"interceptor manifest contract keys are not canonically derived in {context}: "
            f"expected={expected_contract_keys} actual={contract_keys!r}"
        )
    if len(set(contract_keys)) != len(contract_keys):
        fail(f"interceptor manifest contract keys contain duplicates in {context}")


def canonical_manifest_line(manifest: dict[str, Any]) -> str:
    verify_manifest_shape(manifest, "canonical manifest")
    ordered = {field: manifest[field] for field in MANIFEST_FIELDS}
    return json.dumps(ordered, ensure_ascii=False, separators=(",", ":"))


def parse_generated_interceptor_source(text: str, source: Path) -> list[dict[str, Any]]:
    lines = text.splitlines()
    manifest_lines = [
        (index, line.strip()[len(MANIFEST_PREFIX):])
        for index, line in enumerate(lines)
        if line.strip().startswith(MANIFEST_PREFIX)
    ]
    interceptor_lines = [
        index
        for index, line in enumerate(lines)
        if line.strip().startswith("// Intercepted call at ")
    ]
    if not interceptor_lines:
        fail(f"generated interceptor source contains no intercepted calls: {source}")
    if [index for index, _ in manifest_lines] != [index - 1 for index in interceptor_lines]:
        fail(f"every generated interceptor must have exactly one adjacent manifest: {source}")

    manifests: list[dict[str, Any]] = []
    for line_index, payload in manifest_lines:
        context = f"{source}:{line_index + 1}"
        manifest = strict_json_object(payload, context)
        verify_manifest_shape(manifest, context)
        manifests.append(manifest)
    return manifests


def parse_verified_manifest_artifact(text: str, source: Path) -> list[dict[str, Any]]:
    if not text.endswith("\n"):
        fail(f"verified interceptor manifest artifact must end with a newline: {source}")
    lines = text.splitlines()
    if not lines or any(not line for line in lines):
        fail(f"verified interceptor manifest artifact must contain only non-empty JSON lines: {source}")

    manifests: list[dict[str, Any]] = []
    canonical_lines: list[str] = []
    for line_index, line in enumerate(lines, start=1):
        context = f"{source}:{line_index}"
        manifest = strict_json_object(line, context)
        verify_manifest_shape(manifest, context)
        manifests.append(manifest)
        canonical_lines.append(canonical_manifest_line(manifest))

    if lines != sorted(set(canonical_lines)):
        fail(f"verified interceptor manifest artifact is not unique and ordinal-sorted: {source}")
    return manifests


def render_manifest_artifact(manifests: Iterable[dict[str, Any]]) -> str:
    lines = sorted({canonical_manifest_line(manifest) for manifest in manifests})
    if not lines:
        fail("live generator builds emitted no interceptor manifests")
    return "\n".join(lines) + "\n"


def implemented_signal_demo_projects() -> tuple[Path, ...]:
    artifacts = load_artifacts()
    contract = artifacts.load_contract()
    projects: set[Path] = set()
    for item in artifacts.implemented_signal_items(contract):
        evidence_dirs = [
            ROOT / entry
            for entry in item.get("evidence", [])
            if isinstance(entry, str) and entry.startswith("demos/")
        ]
        if not evidence_dirs:
            fail(f"implemented signal item has no demo evidence: {item['key']}")
        item_projects: set[Path] = set()
        for evidence_dir in evidence_dirs:
            candidates = sorted(evidence_dir.glob("*.csproj")) if evidence_dir.is_dir() else []
            if len(candidates) != 1:
                fail(
                    f"implemented signal demo evidence must contain exactly one project for {item['key']}: "
                    f"{evidence_dir.relative_to(ROOT)}"
                )
            item_projects.add(candidates[0])
        projects.update(item_projects)
    return tuple(sorted(projects, key=lambda path: path.as_posix()))


def clean_build_environment() -> dict[str, str]:
    environment = dict(os.environ)
    for key in list(environment):
        if key.startswith("OTEL_") or key.startswith("QYL_"):
            del environment[key]
    environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1"
    environment["DOTNET_NOLOGO"] = "1"
    environment["MSBUILDDISABLENODEREUSE"] = "1"
    return environment


def collect_live_manifest_artifact() -> str:
    projects = implemented_signal_demo_projects()
    if not projects:
        fail("the implemented signal contract selected no demo projects")

    manifests: list[dict[str, Any]] = []
    environment = clean_build_environment()
    with tempfile.TemporaryDirectory(prefix="qyl-generator-manifests-") as temp:
        temp_root = Path(temp)
        artifacts_root = temp_root / "artifacts"
        for project in projects:
            generated_root = temp_root / "generated" / project.stem
            command = [
                "dotnet",
                "build",
                str(project),
                "-c",
                "Release",
                "--disable-build-servers",
                "--artifacts-path",
                str(artifacts_root),
                "-m:1",
                "/nr:false",
                "-v",
                "quiet",
                "-p:NuGetAudit=false",
                "-p:RestoreIgnoreFailedSources=true",
                "-p:UseSharedCompilation=false",
                "-p:WarningsNotAsErrors=NU1801",
                "-p:EmitCompilerGeneratedFiles=true",
                f"-p:CompilerGeneratedFilesOutputPath={generated_root}",
            ]
            completed = subprocess.run(
                command,
                cwd=ROOT,
                env=environment,
                text=True,
                stdout=subprocess.PIPE,
                stderr=subprocess.PIPE,
                check=False,
            )
            if completed.returncode != 0:
                fail(
                    f"generator manifest demo build failed: {project.relative_to(ROOT)}\n"
                    f"exit={completed.returncode}\nstdout={completed.stdout}\nstderr={completed.stderr}"
                )

            generated_files = sorted(
                path
                for path in generated_root.rglob(GENERATED_INTERCEPTOR_FILE)
                if GENERATOR_OUTPUT_SEGMENT in path.parts
            )
            if len(generated_files) > 1:
                fail(
                    f"expected at most one generated interceptor source for {project.relative_to(ROOT)}, "
                    f"found {len(generated_files)}"
                )
            if not generated_files:
                continue
            generated_file = generated_files[0]
            manifests.extend(
                parse_generated_interceptor_source(
                    generated_file.read_text(encoding="utf-8"),
                    generated_file,
                )
            )

    return render_manifest_artifact(manifests)


def main() -> None:
    parser = argparse.ArgumentParser(
        description="Collect canonical interceptor manifests from every implemented signal evidence demo."
    )
    parser.add_argument(
        "--output",
        type=Path,
        help="write the JSONL artifact to this path instead of standard output",
    )
    args = parser.parse_args()

    try:
        artifact = collect_live_manifest_artifact()
    except ManifestCoverageError as error:
        raise SystemExit(str(error)) from error

    if args.output is None:
        print(artifact, end="")
        return

    args.output.parent.mkdir(parents=True, exist_ok=True)
    args.output.write_text(artifact, encoding="utf-8")
    print(f"generator-manifest-coverage-ok: {args.output}")


if __name__ == "__main__":
    main()
