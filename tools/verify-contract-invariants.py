#!/usr/bin/env python3
"""Contract-invariants gate.

Every check here holds under exactly one authority class:

- coherence: it cross-checks two owned artifacts (YAML contract, generated
  outputs, generator source, options source, handoff gate) that no other gate
  compares against each other.
- external contract: it encodes a cited external contract — the Roslyn
  interceptors feature contract, well-known .NET meter/metric names, upstream
  OTEL environment variables, or the generated semconv package surface.
- philosophy guard: it encodes a negative architectural guarantee the repo
  contract mandates (no reflection/profiler/IL-rewrite mechanisms, no inline
  telemetry in the generator, preserved exception semantics, bounded span
  names, centralized sensitive-value capture) that snapshots cannot express.

Positive substring pins over internal member names are intentionally absent:
emitted-source shape is byte-pinned by tools/verify-generator-snapshots.py,
runtime claims are owned by the tools/verify-real-*-demo.py verifiers, and the
public API surface is owned by tools/verify-public-api-baseline.py.
"""
from __future__ import annotations

import functools
import importlib.util
import re
from pathlib import Path
from types import ModuleType
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"
GENERATOR_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "QylAutoInstrumentationGenerator.cs"
INTERCEPTOR_CATALOG_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "QylGeneratedSourceInterceptorCatalog.cs"
OPTIONS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylAutoInstrumentationOptions.cs"
IDS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylAutoInstrumentationIds.cs"
SEMCONV_ATTRIBUTES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylSemanticAttributes.cs"
HANDOFF_GATE_PATH = ROOT / "tools" / "verify-aot-autoinstrumentation-goal.py"
RUNTIME_PROJECT_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
METRIC_METERS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylMetricMeters.cs"
METRIC_NAMES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylMetricNames.cs"
ACTIVITY_NAMES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylActivityNames.cs"
SENSITIVE_CAPTURE_POLICY_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylSensitiveCapturePolicy.cs"
RUNTIME_EMISSION_ROOTS = [
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient",
]
PRODUCTIVE_MECHANISM_ROOTS = [
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.Hosting",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SqlClient",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore",
    ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators",
]
# Philosophy guard: no runtime-dispatch instrumentation substrate (repo contract:
# no CLR profiler, no runtime dispatch, no reflection-based dispatch).
FORBIDDEN_GENERATOR_RUNTIME_DISPATCH_TOKENS = [
    "IOperationInvoker",
    "IServiceBehavior",
    "IOperationBehavior",
    "DispatchOperation",
    "dispatchOperation.Invoker",
    "QylCoreWcf",
]
# Philosophy guard: forbidden instrumentation mechanisms across all productive
# code (repo contract: no profiler, startup hook, ReJIT, IL rewriting, dynamic
# plugin loading, or reflection-based dispatch).
FORBIDDEN_MECHANISM_TOKENS = [
    "CORECLR_PROFILER",
    "DOTNET_STARTUP_HOOKS",
    "ICorProfiler",
    "ReJIT",
    "ILRewrite",
    "ILRewriter",
    "Assembly.Load",
    "Assembly.GetTypes",
    "System.Reflection",
    "Activator.CreateInstance",
    "Type.GetType",
    "GetTypes(",
    "GetFields(",
    "GetProperties(",
    "GetMethods(",
    "GetProperty(",
    "GetField(",
    "PropertyInfo",
    "FieldInfo",
    "MakeGeneric",
    "CallSite",
    "dynamic ",
]
# External contract: https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md
# (referenced by the repo CLAUDE.md). Interceptable locations must come from the
# Roslyn API, never be synthesized.
REQUIRED_ROSLYN_INTERCEPTOR_CONTRACT_TOKENS = [
    "node is InvocationExpressionSyntax",
    "GetInterceptorMethod(invocation, cancellationToken) is not null",
    "GetInterceptableLocation(invocation, cancellationToken)",
    "interceptableLocation is null",
    "GetInterceptsLocationAttributeSyntax(",
    "file sealed class InterceptsLocationAttribute",
]
FORBIDDEN_ROSLYN_INTERCEPTOR_CONTRACT_TOKENS = [
    "new InterceptableLocation",
    "InterceptableLocation.Create",
    "GetLocation()",
    "Location.Create",
]
# Philosophy guard: runtime telemetry must emit attribute keys through the
# generated semconv constants, never literal strings.
FORBIDDEN_ATTRIBUTE_EMISSION_LITERAL_PATTERNS = [
    re.compile(r'\.SetTag\(\s*"[^"]+"'),
    re.compile(r'\.AddTag\(\s*"[^"]+"'),
    re.compile(r'new\s+(?:global::System\.Collections\.Generic\.)?KeyValuePair<string,\s*object\?>\(\s*"[^"]+"'),
]
# Philosophy guard: the generator delegates behavior to the runtime assembly and
# never inlines telemetry into generated code.
FORBIDDEN_GENERATOR_INLINE_TELEMETRY_TOKENS = [
    "QylActivitySource",
    ".SetTag(",
    "new global::System.Diagnostics.Activity",
    "ActivitySource",
]
# Philosophy guard: interceptors must preserve caller exception stack semantics.
FORBIDDEN_EXCEPTION_REWRITE_TOKENS = [
    "throw exception;",
    "throw caughtException;",
    "throw ex;",
]
# Coherence: the explicit NativeAOT boundary — implemented signals whose
# evidence is managed-only. Growing or shrinking this set is a deliberate act.
MANAGED_EVIDENCE_NATIVEAOT_BOUNDARY_KEYS = {
    "signals.logs.LOG4NET",
    "signals.metrics.NSERVICEBUS",
    "signals.traces.KAFKA",
    "signals.traces.MONGODB",
    "signals.traces.NSERVICEBUS",
    "signals.traces.QUARTZ",
    "signals.traces.WCFCLIENT",
}
# External contract: well-known meter names published by .NET / providers that
# the metrics contract registers via intercepted AddMeter. Values, not C#
# constant names — declarations are free to rename.
REQUIRED_REGISTERED_METER_NAME_VALUES = {
    "Microsoft.AspNetCore.Hosting",
    "Microsoft.AspNetCore.Routing",
    "Microsoft.AspNetCore.Diagnostics",
    "Microsoft.AspNetCore.RateLimiting",
    "Microsoft.AspNetCore.HeaderParsing",
    "Microsoft.AspNetCore.Server.Kestrel",
    "Microsoft.AspNetCore.Http.Connections",
    "Microsoft.AspNetCore.Authorization",
    "Microsoft.AspNetCore.Authentication",
    "Microsoft.AspNetCore.Components",
    "Microsoft.AspNetCore.Components.Lifecycle",
    "Microsoft.AspNetCore.Components.Server.Circuits",
    "System.Net.Http",
    "System.Net.NameResolution",
    "Npgsql",
    "NServiceBus.Core",
    "NServiceBus.Core.Pipeline.Incoming",
    "System.Runtime",
    "Qyl.OpenTelemetry.AutoInstrumentation.Database",
}
# External contract: well-known metric instrument names the contract pins.
REQUIRED_METRIC_NAME_VALUES = {
    "aspnetcore.components.navigate",
    "http.server.request.duration",
    "dns.lookup.duration",
}
# External contract: upstream OTEL .NET auto-instrumentation environment
# variable for additional metric sources.
METRICS_ADDITIONAL_SOURCES_VARIABLE = "OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES"
# Philosophy guard: sensitive raw values (query strings, full URLs, query text,
# GraphQL documents) may only be written through the capture policy / owning
# semantics helper, behind the repository's redaction/opt-in controls.
SENSITIVE_RAW_SETTAG_TOKENS = [
    "SetTag(QylSemanticAttributes.UrlQuery",
    "SetTag(QylSemanticAttributes.UrlFull",
    "SetTag(QylSemanticAttributes.DbQueryText",
    "SetTag(QylSemanticAttributes.GraphQlDocument",
]
SENSITIVE_RAW_SETTAG_ALLOWED_PATHS = {
    "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylSensitiveCapturePolicy.cs",
}
SENSITIVE_SEMANTIC_WRITER_TOKENS = [
    "SemanticTagWriter.Set(activity, SemanticAttributes.UrlFull",
    "SemanticTagWriter.Set(activity, SemanticAttributes.UrlQuery",
    "SemanticTagWriter.Set(activity, SemanticAttributes.GraphQlDocument",
]
SENSITIVE_SEMANTIC_WRITER_ALLOWED_PATH = "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Semantics/HttpSemantics.cs"
URL_FORMAT_ALLOWED_PATHS = {
    "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylCaptureHelpers.cs",
    "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylSensitiveCapturePolicy.cs",
    "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Semantics/HttpSemantics.cs",
}
DB_QUERY_TEXT_ALLOWED_PATHS = {
    "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/EntityFrameworkCore/EntityFrameworkCoreDiagnosticListener.cs",
    "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/SqlClient/SqlClientDiagnosticListener.cs",
    "src/Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore/EntityFrameworkCoreDiagnosticListener.cs",
    "src/Qyl.OpenTelemetry.AutoInstrumentation.SqlClient/SqlClientDiagnosticListener.cs",
}
QYL_ABI_DELEGATION_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.GeneratedCode.QylIntercepted"


def fail(message: str) -> None:
    raise SystemExit(message)


def load_artifacts() -> ModuleType:
    spec = importlib.util.spec_from_file_location("qyl_contract_artifacts", ARTIFACTS_PATH)
    if spec is None or spec.loader is None:
        fail(f"cannot load contract artifact generator: {ARTIFACTS_PATH}")
    module = importlib.util.module_from_spec(spec)
    spec.loader.exec_module(module)
    return module


def generator_partial_paths():
    # The generator is split across QylAutoInstrumentationGenerator{,.Descriptors,.Detection,.Shapes}.cs partials.
    return sorted(GENERATOR_PATH.parent.glob("QylAutoInstrumentationGenerator*.cs"))


@functools.cache
def read_generator_sources() -> str:
    sources = [p.read_text() for p in generator_partial_paths()]
    sources.append(INTERCEPTOR_CATALOG_PATH.read_text())
    return "\n".join(sources)


def parse_instrumentation_id_constants() -> dict[str, str]:
    text = IDS_PATH.read_text()
    return {
        match.group(2): match.group(1)
        for match in re.finditer(r'public const string ([A-Za-z0-9]+) = "([A-Z0-9]+)";', text)
    }


def parse_options_id_array(options: str, name: str, id_constants: dict[str, str]) -> set[str]:
    try:
        block = options.split(f"private static readonly string[] {name}", 1)[1].split("];", 1)[0]
    except IndexError:
        fail(f"{name} array missing from QylAutoInstrumentationOptions")

    values: set[str] = set()
    for constant_name in re.findall(r"QylAutoInstrumentationIds\.([A-Za-z0-9]+)", block):
        for instrumentation_id, candidate_name in id_constants.items():
            if candidate_name == constant_name:
                values.add(instrumentation_id)
                break
        else:
            fail(f"{name} references unknown QylAutoInstrumentationIds.{constant_name}")

    return values


def parse_string_constants(text: str) -> dict[str, str]:
    return {
        match.group(1): match.group(2)
        for match in re.finditer(r'const string ([A-Za-z0-9_]+) = "([^"]*)";', text)
    }


def verify_contract_artifacts(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    artifacts.verify_contract_model(contract)
    artifacts.verify_generated_files(contract)


def verify_compile_binding_only_truth_gate(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    # Data-level re-enforcement of the compile-binding truth predicate: an
    # implemented compile_binding_only signal needs an explicit allowlist entry
    # and must not claim runtime verification evidence. Enforced here directly
    # so weakening the artifacts module cannot silently drop the predicate.
    allowlist = set(getattr(artifacts, "IMPLEMENTED_COMPILE_BINDING_ONLY_ALLOWLIST"))
    unexpected_compile_binding = sorted(
        str(item["key"])
        for item in contract["contract_items"]
        if item.get("qyl_status") == "implemented"
        and item.get("evidence_level") == "compile_binding_only"
        and str(item["key"]) not in allowlist
    )
    if unexpected_compile_binding:
        fail(
            "implemented compile_binding_only signals must be explicit allowlist entries: "
            f"{unexpected_compile_binding}"
        )

    for item in contract["contract_items"]:
        if item.get("evidence_level") != "compile_binding_only":
            continue
        runtime_evidence = [
            entry
            for entry in item.get("evidence", [])
            if isinstance(entry, str)
            and (
                entry.startswith("tools/verify-real-")
                or entry
                in {
                    "tools/smoketest.sh",
                    "tools/verify-nativeaot-consumer.py",
                    "tools/verify-webapi-aot-demo.py",
                }
            )
        ]
        if runtime_evidence:
            fail(
                "compile_binding_only item must not claim runtime verification evidence "
                f"for {item['key']}: {runtime_evidence}"
            )


def verify_conformance_profile_gate(artifacts: ModuleType) -> None:
    # Data-level enforcement over the conformance profile set: fixture coverage
    # must stay multi-profile, unique, and honestly shaped.
    required_profile_ids = set(getattr(artifacts, "REQUIRED_CONFORMANCE_PROFILE_IDS"))
    profiles = list(getattr(artifacts, "CONFORMANCE_PROFILES"))
    profile_ids = [str(profile["profile_id"]) for profile in profiles]
    service_names = [str(profile["service_name"]) for profile in profiles]
    if len(set(profile_ids)) != len(profile_ids):
        fail("duplicate conformance profile_id")
    if len(set(service_names)) != len(service_names):
        fail("duplicate conformance service_name")
    if set(profile_ids) != required_profile_ids:
        fail(
            "conformance profiles must match required fixture profile ids: "
            f"missing={sorted(required_profile_ids - set(profile_ids))} "
            f"stale={sorted(set(profile_ids) - required_profile_ids)}"
        )
    if len(profile_ids) < 8:
        fail("conformance plan must not collapse back to a narrow single-demo profile set")
    for profile in profiles:
        profile_id = str(profile["profile_id"])
        signal_names = list(profile["signal_names"])
        if profile_id == "qyl-aot-unsupported-nativeaot":
            if signal_names:
                fail("unsupported NativeAOT conformance profile must remain empty")
        elif not signal_names:
            fail(f"conformance profile must remain non-empty: {profile_id}")


def verify_managed_evidence_boundaries(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    managed_keys = {
        str(item["key"])
        for item in artifacts.implemented_signal_items(contract)
        if item.get("evidence_level") == "verified_managed"
    }
    expected_boundary_keys = MANAGED_EVIDENCE_NATIVEAOT_BOUNDARY_KEYS
    if managed_keys != expected_boundary_keys:
        fail(
            "verified_managed implemented signals must be an explicit NativeAOT boundary set: "
            f"missing_boundary={sorted(managed_keys - expected_boundary_keys)} "
            f"stale_boundary={sorted(expected_boundary_keys - managed_keys)}"
        )


def verify_handoff_real_demo_coverage(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    evidence_real_demo_verifiers = {
        evidence
        for item in artifacts.implemented_signal_items(contract)
        for evidence in item.get("evidence", [])
        if isinstance(evidence, str)
        and evidence.startswith("tools/verify-real-")
        and evidence.endswith("-demo.py")
    }
    handoff_gate = HANDOFF_GATE_PATH.read_text()
    missing = sorted(path for path in evidence_real_demo_verifiers if path not in handoff_gate)
    if missing:
        fail(f"real demo evidence missing from whole-goal handoff gate: {missing}")
    handoff_real_demo_verifiers = set(re.findall(r'"(tools/verify-real-[^"]+-demo\.py)"', handoff_gate))
    unclaimed = sorted(path for path in handoff_real_demo_verifiers if path not in evidence_real_demo_verifiers)
    if unclaimed:
        fail(f"whole-goal handoff real demo verifier is not claimed by contract evidence: {unclaimed}")


def verify_nativeaot_evidence_is_executable(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    for item in artifacts.implemented_signal_items(contract):
        if item.get("evidence_level") != "verified_nativeaot":
            continue

        verifier_paths = [
            evidence
            for evidence in item.get("evidence", [])
            if isinstance(evidence, str)
            and evidence.startswith("tools/verify")
            and evidence.endswith(".py")
        ]
        native_verifiers: list[str] = []
        for verifier_path in verifier_paths:
            verifier = ROOT / verifier_path
            text = verifier.read_text()
            if (
                "run_nativeaot" in text
                and "nativeaot" in text.lower()
                and '"nativeaot"' in text
                and "verify_report(" in text
            ):
                native_verifiers.append(verifier_path)

        if not native_verifiers:
            fail(f"verified_nativeaot item has no executable NativeAOT verifier evidence: {item['key']}")


def verify_environment_contract(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    items = artifacts.contract_items(contract)
    options = OPTIONS_PATH.read_text()
    id_constants = parse_instrumentation_id_constants()

    controls = [item for item in items if item["kind"] == artifacts.CONTROL_KIND]
    instrumentation_options = [item for item in items if item["kind"] == artifacts.OPTION_KIND]
    if len(controls) != 7:
        fail(f"wrong YAML global environment control count: {len(controls)}")
    if len(instrumentation_options) != 16:
        fail(f"wrong YAML instrumentation option count: {len(instrumentation_options)}")

    for item in controls:
        variable = str(item["environment_variable"])
        if "{0}" in variable:
            prefix, suffix = variable.split("{0}", 1)
            expected_expression = f'"{prefix}" + instrumentationId + "{suffix}"'
            if expected_expression not in options:
                fail(f"global signal-specific control is not generated by QylAutoInstrumentationOptions: {variable}")
        elif variable not in options:
            fail(f"global control is not read by QylAutoInstrumentationOptions: {variable}")

    for item in instrumentation_options:
        if item.get("qyl_status") != "option_bound":
            continue
        variable = str(item["environment_variable"])
        if variable not in options:
            fail(f"instrumentation option is not read by QylAutoInstrumentationOptions: {variable}")

    expected_by_signal = {
        signal: {
            str(item["instrumentation_id"])
            for item in items
            if item["kind"] == artifacts.SIGNAL_KIND and item.get("signal") == signal
        }
        for signal in ["traces", "metrics", "logs"]
    }
    actual_by_signal = {
        "traces": parse_options_id_array(options, "TraceInstrumentationIds", id_constants),
        "metrics": parse_options_id_array(options, "MetricInstrumentationIds", id_constants),
        "logs": parse_options_id_array(options, "LogInstrumentationIds", id_constants),
    }
    for signal, expected in expected_by_signal.items():
        actual = actual_by_signal[signal]
        if expected != actual:
            fail(
                f"{signal} instrumentation id array mismatch: "
                f"missing={sorted(expected - actual)} extra={sorted(actual - expected)}"
            )


def verify_semconv_attribute_contract() -> None:
    attributes = SEMCONV_ATTRIBUTES_PATH.read_text()
    runtime_project = RUNTIME_PROJECT_PATH.read_text()

    for package in [
        'Include="Qyl.OpenTelemetry.SemanticConventions"',
        'Include="Qyl.OpenTelemetry.SemanticConventions.Incubating"',
    ]:
        if package not in runtime_project:
            fail(f"runtime project must reference generated semconv package: {package}")

    for namespace in [
        "Qyl.OpenTelemetry.SemanticConventions.Attributes.",
        "Qyl.OpenTelemetry.SemanticConventions.Incubating.Attributes.",
    ]:
        if namespace not in attributes:
            fail(f"QylSemanticAttributes must alias generated semconv constants from {namespace}")

    for root in RUNTIME_EMISSION_ROOTS:
        for path in root.rglob("*.cs"):
            text = path.read_text()
            for pattern in FORBIDDEN_ATTRIBUTE_EMISSION_LITERAL_PATTERNS:
                match = pattern.search(text)
                if match is not None:
                    fail(f"runtime telemetry attribute emission must not use literal keys: {path.relative_to(ROOT)}")


def verify_metric_contract() -> None:
    meters = METRIC_METERS_PATH.read_text()
    names = METRIC_NAMES_PATH.read_text()
    options = OPTIONS_PATH.read_text()
    generator = read_generator_sources()

    meter_constants = parse_string_constants(meters)
    registered_meter_values = {
        meter_constants[constant_name]
        for constant_name in re.findall(r"\.Add\(([A-Za-z0-9_]+)\);", meters)
        if constant_name in meter_constants
    }
    missing_meters = REQUIRED_REGISTERED_METER_NAME_VALUES - registered_meter_values
    if missing_meters:
        fail(f"QylMetricMeters must register required well-known meter names: {sorted(missing_meters)}")

    metric_name_values = set(parse_string_constants(names).values())
    missing_metric_names = REQUIRED_METRIC_NAME_VALUES - metric_name_values
    if missing_metric_names:
        fail(f"QylMetricNames must pin required well-known metric names: {sorted(missing_metric_names)}")

    if METRICS_ADDITIONAL_SOURCES_VARIABLE not in options:
        fail(
            "QylAutoInstrumentationOptions must preserve upstream additional metric source support: "
            f"{METRICS_ADDITIONAL_SOURCES_VARIABLE}"
        )
    # Coherence: the option read from the additional-sources variable must flow
    # into meter registration. Both member names are derived from the variable
    # value, so renames stay free.
    variable_constant = re.search(
        rf'const string ([A-Za-z0-9_]+) = "{METRICS_ADDITIONAL_SOURCES_VARIABLE}";',
        options,
    )
    if variable_constant is None:
        fail("additional metric sources variable must be a named constant in QylAutoInstrumentationOptions")
    read_sites = options.split(variable_constant.group(1))
    additional_sources_member = None
    for match in re.finditer(r"internal string\[\] ([A-Za-z0-9_]+)", options):
        if any(match.group(1) in segment for segment in read_sites):
            additional_sources_member = match.group(1)
    if additional_sources_member is None or f"options.{additional_sources_member}" not in meters:
        fail("QylMetricMeters must append the additional metric sources option to registered meters")

    for token in ["NavigationManager", "NavigateTo"]:
        if token in generator or token in meters or token in names:
            fail(f"productive code must not synthesize source-visible ASP.NET Core component metrics: {token}")


def verify_sensitive_attribute_emission_policy() -> None:
    # The repository's redaction/opt-in controls (repo contract) must gate every
    # sensitive raw value. Positive anchors are limited to the control points.
    policy = SENSITIVE_CAPTURE_POLICY_PATH.read_text()
    for token in [
        "QylCaptureHelpers.RedactQueryValues(",
        "AspNetCoreUrlQueryRedactionDisabled",
        "HttpClientUrlQueryRedactionDisabled",
        "GraphQlSetDocument",
    ]:
        if token not in policy:
            fail(f"QylSensitiveCapturePolicy must implement the redaction/opt-in controls: {token}")

    for root in RUNTIME_EMISSION_ROOTS:
        for path in root.rglob("*.cs"):
            relative_path = path.relative_to(ROOT).as_posix()
            text = path.read_text()

            for token in SENSITIVE_RAW_SETTAG_TOKENS:
                if token in text and relative_path not in SENSITIVE_RAW_SETTAG_ALLOWED_PATHS:
                    fail(f"sensitive raw attribute writes must go through QylSensitiveCapturePolicy: {relative_path} {token}")

            for token in SENSITIVE_SEMANTIC_WRITER_TOKENS:
                if token in text and relative_path != SENSITIVE_SEMANTIC_WRITER_ALLOWED_PATH:
                    fail(f"runtime-public sensitive writes must go through the owning semantics helper: {relative_path} {token}")

            if "QylCaptureHelpers.FormatUrlFull(" in text and relative_path not in URL_FORMAT_ALLOWED_PATHS:
                fail(f"url.full formatting must stay centralized behind sensitive capture policy/HttpSemantics: {relative_path}")

            if "QylCaptureHelpers.RedactQueryValues(" in text and relative_path not in URL_FORMAT_ALLOWED_PATHS:
                fail(f"url query redaction must stay centralized behind sensitive capture policy/helpers: {relative_path}")

            if "SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText" not in text:
                continue

            if relative_path not in DB_QUERY_TEXT_ALLOWED_PATHS:
                fail(f"db.query.text writes must be owned by typed DB listener paths: {relative_path}")

            for match in re.finditer(r"SemanticTagWriter\.Set\(activity,\s*SemanticAttributes\.DbQueryText", text):
                guard_window = text[max(0, match.start() - 260):match.start()]
                if "if (DatabaseSemantics.ShouldWriteQueryText(" not in guard_window:
                    fail(f"db.query.text write must be guarded by DatabaseSemantics.ShouldWriteQueryText: {relative_path}")


def verify_bounded_activity_name_policy() -> None:
    # Philosophy guard (repo contract: span names stay bounded): public span-name
    # composers must not accept unbounded inputs.
    names = ACTIVITY_NAMES_PATH.read_text()
    forbidden_parameter_fragments = ["url", "uri", "path", "query", "exception", "message", "statement", "text"]
    for match in re.finditer(r"public static string [A-Za-z0-9]+\(([^)]*)\)", names):
        parameters = [
            parameter.strip().split(" ")[-1].strip("?")
            for parameter in match.group(1).split(",")
            if parameter.strip()
        ]
        for parameter in parameters:
            lowered = parameter.lower()
            for fragment in forbidden_parameter_fragments:
                if fragment in lowered:
                    fail(f"QylActivityNames public span-name composer must not accept unbounded parameter '{parameter}'")


def find_catch_blocks(text: str) -> list[str]:
    blocks: list[str] = []
    position = 0
    while True:
        match = re.search(r"\bcatch\b", text[position:])
        if match is None:
            return blocks

        catch_index = position + match.start()
        brace_index = text.find("{", catch_index)
        if brace_index < 0:
            fail("catch block without body")

        depth = 0
        for index in range(brace_index, len(text)):
            char = text[index]
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    blocks.append(text[brace_index:index + 1])
                    position = index + 1
                    break
        else:
            fail("unterminated catch block")


def verify_behavior_semantics_contract() -> None:
    generator = read_generator_sources()
    if QYL_ABI_DELEGATION_TOKEN not in generator:
        fail("generator must delegate intercepted call-sites to the Qyl runtime instrumentation assembly")

    for token in FORBIDDEN_GENERATOR_INLINE_TELEMETRY_TOKENS:
        if token in generator:
            fail(f"generator must not inline telemetry behavior instead of delegating to runtime: {token}")

    behavior_sources = [*generator_partial_paths(), *sorted((ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation").glob("QylIntercepted*.cs"))]
    for path in behavior_sources:
        text = path.read_text()
        for token in FORBIDDEN_EXCEPTION_REWRITE_TOKENS:
            if token in text:
                fail(f"interceptor must preserve exception stack semantics; forbidden token in {path.relative_to(ROOT)}: {token}")

        for block in find_catch_blocks(text):
            if "throw;" not in block:
                fail(f"interceptor catch block must rethrow with throw; in {path.relative_to(ROOT)}")


def strip_csharp_comments(text: str) -> str:
    result: list[str] = []
    index = 0
    in_block_comment = False
    while index < len(text):
        if in_block_comment:
            if text.startswith("*/", index):
                in_block_comment = False
                index += 2
            else:
                if text[index] in "\r\n":
                    result.append(text[index])
                index += 1
            continue

        if text.startswith("/*", index):
            in_block_comment = True
            index += 2
            continue

        if text.startswith("//", index):
            while index < len(text) and text[index] not in "\r\n":
                index += 1
            continue

        result.append(text[index])
        index += 1

    return "".join(result)


def verify_productive_mechanism_contract() -> None:
    for root in PRODUCTIVE_MECHANISM_ROOTS:
        for path in sorted(root.rglob("*")):
            if "bin" in path.parts or "obj" in path.parts:
                continue

            if path.suffix not in {".cs", ".props", ".targets"}:
                continue

            text = path.read_text()
            scan_text = strip_csharp_comments(text) if path.suffix == ".cs" else text
            for token in FORBIDDEN_MECHANISM_TOKENS:
                if token in scan_text:
                    fail(f"productive code must not use forbidden instrumentation mechanism {token}: {path.relative_to(ROOT)}")


def parse_interceptor_kinds(generator: str) -> set[str]:
    try:
        enum_block = generator.split("private enum InterceptorKind", 1)[1].split("}", 1)[0]
    except IndexError:
        fail("InterceptorKind enum missing from generator")

    return {
        match.group(1)
        for match in re.finditer(r"^\s*([A-Za-z0-9]+),\s*$", enum_block, re.MULTILINE)
    }


def parse_emission_descriptor_kinds(generator: str) -> set[str]:
    descriptor_kinds = {
        match.group(1)
        for match in re.finditer(
            r"new\s+InterceptorEmissionDescriptor\(\s*InterceptorKind\.([A-Za-z0-9]+)",
            generator,
        )
    }
    if not descriptor_kinds:
        fail("s_emissionDescriptors must describe every InterceptorKind")

    return descriptor_kinds


def parse_emission_descriptor_bodies(generator: str) -> dict[str, str]:
    bodies_by_kind: dict[str, str] = {}
    matches = list(re.finditer(
        r"new\s+InterceptorEmissionDescriptor\(\s*InterceptorKind\.([A-Za-z0-9]+),\s*new\s+([A-Za-z0-9]+BodyDescriptor)\(",
        generator,
    ))
    constructor_count = len(re.findall(r"new\s+InterceptorEmissionDescriptor\(", generator))
    if len(matches) != constructor_count:
        fail(
            "every emission descriptor must construct exactly one typed body descriptor: "
            f"{constructor_count} constructors, {len(matches)} with a parsable body"
        )

    for match in matches:
        kind = match.group(1)
        if kind in bodies_by_kind:
            fail(f"duplicate emission descriptor for InterceptorKind.{kind}")
        bodies_by_kind[kind] = match.group(2)

    if not bodies_by_kind:
        fail("s_emissionDescriptors must map every InterceptorKind to a typed body descriptor")

    return bodies_by_kind


def parse_db_instrumentation_ids(generator: str) -> set[str]:
    match = re.search(
        r"private static string GetDbInstrumentationId\([^)]*\)\s*\{(?P<body>.*?)\n    \}",
        generator,
        re.DOTALL,
    )
    if match is None:
        fail("GetDbInstrumentationId helper missing from generator")

    ids = set(re.findall(r'return "([A-Z0-9]+)";', match.group("body")))
    if not ids:
        fail("GetDbInstrumentationId must return database instrumentation ids")

    return ids


def parse_db_trace_contract_keys(generator: str) -> set[str]:
    if re.search(r"TelemetrySignal\.Traces,\s*\n\s*instrumentationId,", generator) is None:
        fail("DbCommand target must derive its trace contract key from GetDbInstrumentationId")

    return {f"signals.traces.{db_id}" for db_id in parse_db_instrumentation_ids(generator)}


def parse_additional_metric_id_keys(generator: str) -> set[str]:
    # Additional signal claims are always metrics and are declared structurally
    # as MetricIds("<INSTRUMENTATION_ID>", ...) — never as freeform key strings.
    keys: set[str] = set()
    for match in re.finditer(r"MetricIds\((.*?)\)", generator, re.DOTALL):
        keys.update(f"signals.metrics.{m}" for m in re.findall(r'"([A-Z0-9]+)"', match.group(1)))

    return keys


def parse_db_metric_id_keys(generator: str) -> set[str]:
    match = re.search(
        r"private static [^=]+ GetDbMetricIds\([^)]*\)\s*=>.*?};",
        generator,
        re.DOTALL,
    )
    if match is None:
        fail("GetDbMetricIds helper missing from generator")

    return {f"signals.metrics.{m}" for m in re.findall(r'"([A-Z0-9]+)"', match.group(0))}


def verify_matcher_registration(generator: str) -> None:
    detection_methods = set(re.findall(r"private static bool (TryGet[A-Za-z0-9]+Invocation)\(", generator))
    if not detection_methods:
        fail("no TryGet*Invocation detection methods found in the generator")

    registered = set(re.findall(r"new InterceptorMatcherDescriptor\((TryGet[A-Za-z0-9]+Invocation)\)", generator))
    unknown = registered - detection_methods
    if unknown:
        fail(f"matcher rows reference unknown detection methods: {sorted(unknown)}")

    for method in sorted(detection_methods - registered):
        references = len(re.findall(rf"\b{method}\b", generator)) - 1
        if references <= 0:
            fail(
                "detection method is neither registered in CreateGeneratedMatcherDescriptors "
                f"nor called by another detection method: {method}"
            )


def verify_interceptor_emission_bodies(generator: str, kinds: set[str]) -> None:
    bodies_by_kind = parse_emission_descriptor_bodies(generator)
    missing_bodies = kinds - set(bodies_by_kind)
    if missing_bodies:
        fail(f"InterceptorKind values missing emission body descriptors: {sorted(missing_bodies)}")

    for token in [
        "private abstract record InterceptorBodyDescriptor",
        "public abstract void Emit(StringBuilder builder, in InterceptedInvocation invocation, int index);",
        "InterceptorBodyDescriptor Body",
        "Unsupported interceptor kind: ",
    ]:
        if token not in generator:
            fail(f"generator must model emission bodies as a closed self-emitting descriptor hierarchy: {token}")


def collect_generator_target_contract_keys(generator: str) -> set[str]:
    # Primary keys are derived, never declared: each target names a
    # TelemetrySignal and an InstrumentationId; the key is composed here.
    signal_names = {"Traces": "traces", "Metrics": "metrics", "Logs": "logs"}
    target_contract_keys = {
        f"signals.{signal_names[match.group(1)]}.{match.group(2)}"
        for match in re.finditer(
            r"TelemetrySignal\.(Traces|Metrics|Logs),\s*\n\s*\"([A-Z0-9]+)\",",
            generator,
        )
    }
    if not target_contract_keys:
        fail("no structurally derived target contract keys found in the generator")
    target_contract_keys.update(parse_additional_metric_id_keys(generator))
    target_contract_keys.update(parse_db_trace_contract_keys(generator))

    if "GetDbMetricIds(instrumentationId)" not in generator:
        fail("DbCommand target must route metric contract keys through GetDbMetricIds")
    target_contract_keys.update(parse_db_metric_id_keys(generator))
    return target_contract_keys


def verify_interceptor_target_coverage(generator: str, implemented_signal_keys: set[str]) -> None:
    kinds = parse_interceptor_kinds(generator)
    if not kinds:
        fail("InterceptorKind enum has no values")

    try:
        dispatch_block = generator.split("for (var index = 0; index < invocations.Length; index++)", 1)[1].split(
            'context.AddSource("QylAutoInstrumentation.Interceptors.g.cs"', 1
        )[0]
    except IndexError:
        fail("EmitInterceptors dispatch block missing")

    if "GetEmissionDescriptor(invocation.Target)" not in dispatch_block:
        fail("emitter dispatch must use the descriptor table")
    if "private delegate void InterceptorEmitter" in generator or "InterceptorEmitter? Emitter" in generator or "descriptor.Emitter" in dispatch_block:
        fail("emitter dispatch must not use generic emitter delegates")
    # Q4: bodies emit themselves; body-type exhaustiveness is enforced by the
    # compiler through the abstract Emit member, not by this gate.
    if "descriptor.Body.Emit(builder, in invocation, index);" not in dispatch_block:
        fail("emitter dispatch must invoke the polymorphic body emit")

    try:
        matcher_dispatch_block = generator.split("private static bool TryGetInvocation(", 1)[1].split(
            "private static void EmitInterceptors(",
            1,
        )[0]
    except IndexError:
        fail("TryGetInvocation matcher dispatch block missing")

    if "foreach (var descriptor in s_matcherDescriptors)" not in matcher_dispatch_block:
        fail("matcher dispatch must use the descriptor table")
    if "descriptor.TryMatch(symbol, receiverType, out target)" not in matcher_dispatch_block:
        fail("matcher dispatch must invoke descriptor.TryMatch")
    if re.search(r"if\s*\(\s*TryGet[A-Za-z0-9]+Invocation\(", matcher_dispatch_block) is not None:
        fail("matcher dispatch must not reintroduce hand-coded TryGet*Invocation sequencing")

    verify_matcher_registration(generator)

    descriptor_kinds = parse_emission_descriptor_kinds(generator)
    descriptor_missing = kinds - descriptor_kinds
    descriptor_extra = descriptor_kinds - kinds
    if descriptor_missing or descriptor_extra:
        fail(
            "InterceptorKind descriptor mismatch: "
            f"missing={sorted(descriptor_missing)} extra={sorted(descriptor_extra)}"
        )

    target_missing = {
        kind
        for kind in kinds
        if f"InterceptorKind.{kind}," not in generator and f"= InterceptorKind.{kind};" not in generator
    }
    if target_missing:
        fail(f"InterceptorKind values missing from target construction: {sorted(target_missing)}")

    verify_interceptor_emission_bodies(generator, kinds)

    target_contract_keys = collect_generator_target_contract_keys(generator)
    missing_contract_keys = implemented_signal_keys - target_contract_keys
    extra_contract_keys = target_contract_keys - implemented_signal_keys
    if missing_contract_keys or extra_contract_keys:
        fail(
            "generator target contract key mismatch: "
            f"missing={sorted(missing_contract_keys)} extra={sorted(extra_contract_keys)}"
        )


def verify_generator_keys(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    generator = read_generator_sources()
    # Q3: keys are derived from TelemetrySignal + InstrumentationId. Freeform
    # key literals are unrepresentable — their reappearance is a regression.
    if re.search(r'"signals\.(?:traces|metrics|logs)\.', generator) is not None:
        fail("generator must derive contract keys structurally, never declare freeform key literals")
    generator_keys = collect_generator_target_contract_keys(generator)
    implemented_signal_keys = {str(item["key"]) for item in artifacts.implemented_signal_items(contract)}
    source_interceptor_signal_keys = {
        str(item["key"])
        for item in artifacts.source_interceptor_signal_items(contract)
    }
    unsupported_keys = {str(item["key"]) for item in artifacts.unsupported_signal_items(contract)}
    if generator_keys != implemented_signal_keys:
        fail(
            "generator signal key mismatch: "
            f"missing={sorted(implemented_signal_keys - generator_keys)} "
            f"extra={sorted(generator_keys - implemented_signal_keys)}"
        )

    if generator_keys & unsupported_keys:
        fail(f"unsupported keys leaked into generator: {sorted(generator_keys & unsupported_keys)}")

    for token in FORBIDDEN_GENERATOR_RUNTIME_DISPATCH_TOKENS:
        if token in generator:
            fail(f"generator must not emit runtime dispatch instrumentation: {token}")

    for token in REQUIRED_ROSLYN_INTERCEPTOR_CONTRACT_TOKENS:
        if token not in generator:
            fail(f"generator must preserve Roslyn interceptor contract token: {token}")

    for token in FORBIDDEN_ROSLYN_INTERCEPTOR_CONTRACT_TOKENS:
        if token in generator:
            fail(f"generator must not synthesize interceptor locations: {token}")

    if "InterceptsLocationAttribute(" in generator and "GetInterceptsLocationAttributeSyntax(" not in generator:
        fail("generator must emit InterceptsLocationAttribute through Roslyn GetInterceptsLocationAttributeSyntax")

    verify_interceptor_target_coverage(generator, implemented_signal_keys)
    generator_target_keys = collect_generator_target_contract_keys(generator)
    missing_source_interceptor_bindings = source_interceptor_signal_keys - generator_target_keys
    if missing_source_interceptor_bindings:
        fail(f"source_interceptor contract keys missing generator binding: {sorted(missing_source_interceptor_bindings)}")


def main() -> None:
    artifacts = load_artifacts()
    contract = artifacts.load_contract()
    verify_contract_artifacts(artifacts, contract)
    verify_compile_binding_only_truth_gate(artifacts, contract)
    verify_conformance_profile_gate(artifacts)
    verify_managed_evidence_boundaries(artifacts, contract)
    verify_handoff_real_demo_coverage(artifacts, contract)
    verify_nativeaot_evidence_is_executable(artifacts, contract)
    verify_generator_keys(artifacts, contract)
    verify_environment_contract(artifacts, contract)
    verify_semconv_attribute_contract()
    verify_metric_contract()
    verify_sensitive_attribute_emission_policy()
    verify_bounded_activity_name_policy()
    verify_behavior_semantics_contract()
    verify_productive_mechanism_contract()
    print("contract-invariants-ok")


if __name__ == "__main__":
    main()
