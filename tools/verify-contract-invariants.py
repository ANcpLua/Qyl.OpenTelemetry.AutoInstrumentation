#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import re
from pathlib import Path
from types import ModuleType
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"
CONTRACT_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "InstrumentationContract.cs"
GENERATOR_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "QylAutoInstrumentationGenerator.cs"
INTERCEPTOR_CATALOG_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "QylGeneratedSourceInterceptorCatalog.cs"
OPTIONS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylAutoInstrumentationOptions.cs"
IDS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylAutoInstrumentationIds.cs"
SEMCONV_ATTRIBUTES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylSemanticAttributes.cs"
SEMCONV_GENERATOR_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators" / "SemConvRegistryGenerator.cs"
RUNTIME_SEMANTICS_PATH = ROOT / "docs" / "RUNTIME_SEMANTICS.md"
HANDOFF_GATE_PATH = ROOT / "tools" / "verify-aot-autoinstrumentation-goal.py"
RUNTIME_PROJECT_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Qyl.OpenTelemetry.AutoInstrumentation.csproj"
ACTIVITY_STATUS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylActivityStatus.cs"
METRIC_METERS_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylMetricMeters.cs"
METRIC_NAMES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylMetricNames.cs"
ACTIVITY_NAMES_PATH = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "QylActivityNames.cs"
DIAGNOSTIC_SEMANTICS_ROOT = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Semantics"
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
FORBIDDEN_GENERATOR_RUNTIME_DISPATCH_TOKENS = [
    "IOperationInvoker",
    "IServiceBehavior",
    "IOperationBehavior",
    "DispatchOperation",
    "dispatchOperation.Invoker",
    "QylCoreWcf",
]
FORBIDDEN_GENERATOR_MECHANISM_TOKENS = [
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
    "MakeGeneric",
    "CallSite",
    "dynamic ",
]
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
FORBIDDEN_ATTRIBUTE_EMISSION_LITERAL_PATTERNS = [
    re.compile(r'\.SetTag\(\s*"[^"]+"'),
    re.compile(r'\.AddTag\(\s*"[^"]+"'),
    re.compile(r'new\s+(?:global::System\.Collections\.Generic\.)?KeyValuePair<string,\s*object\?>\(\s*"[^"]+"'),
]
FORBIDDEN_GENERATOR_INLINE_TELEMETRY_TOKENS = [
    "QylActivitySource",
    ".SetTag(",
    "new global::System.Diagnostics.Activity",
    "ActivitySource",
]
FORBIDDEN_EXCEPTION_REWRITE_TOKENS = [
    "throw exception;",
    "throw caughtException;",
    "throw ex;",
]
MANAGED_EVIDENCE_NATIVEAOT_BOUNDARY_TOKENS = {
    "signals.traces.NSERVICEBUS": [
        "NServiceBus",
        "NativeAOT is structurally blocked",
        "Reflection.Emit",
        "ConcreteProxyCreator",
        "Particular/NServiceBus#7817",
    ],
    "signals.metrics.NSERVICEBUS": [
        "NServiceBus",
        "NativeAOT is structurally blocked",
        "Reflection.Emit",
        "ConcreteProxyCreator",
        "Particular/NServiceBus#7817",
    ],
    "signals.traces.WCFCLIENT": [
        "WCF client",
        "NativeAOT is blocked",
        "DispatchProxy",
        "PlatformNotSupportedException",
    ],
}
REQUIRED_METER_PROVIDER_DELEGATION_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.QylMetricMeters.GetEnabledMeterNames()"
REQUIRED_INTERCEPTOR_EMITTER_DELEGATION_TOKEN = "global::Qyl.OpenTelemetry.AutoInstrumentation.QylIntercepted"
FORBIDDEN_PRODUCTIVE_MECHANISM_TOKENS = FORBIDDEN_GENERATOR_MECHANISM_TOKENS


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


def verify_contract_artifacts(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    artifacts.verify_contract_model(contract)
    artifacts.verify_generated_files(contract)


def verify_compile_binding_only_truth_gate(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    artifacts_source = ARTIFACTS_PATH.read_text()
    for token in [
        "IMPLEMENTED_COMPILE_BINDING_ONLY_ALLOWLIST",
        "implemented compile_binding_only signals require an explicit allowlist entry",
        'evidence_level != "compile_binding_only"',
        "compile_binding_only item must not claim runtime verification evidence",
        '"tools/verify-source-interceptor-consumer.py"',
        '"tools/verify-nativeaot-consumer.py"',
        '"tools/verify-webapi-aot-demo.py"',
    ]:
        if token not in artifacts_source:
            fail(f"contract generator must preserve compile_binding_only truth gate token: {token}")

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


def verify_conformance_profile_gate(artifacts: ModuleType) -> None:
    artifacts_source = ARTIFACTS_PATH.read_text()
    for token in [
        "REQUIRED_CONFORMANCE_PROFILE_IDS",
        "conformance plan must preserve multi-profile fixture coverage",
        "conformance profile must expect at least one signal",
        "unsupported NativeAOT conformance profile must expect no signals",
        "duplicate conformance profile_id",
        "duplicate conformance service_name",
    ]:
        if token not in artifacts_source:
            fail(f"contract generator must preserve conformance profile gate token: {token}")

    required_profile_ids = set(getattr(artifacts, "REQUIRED_CONFORMANCE_PROFILE_IDS"))
    profiles = list(getattr(artifacts, "CONFORMANCE_PROFILES"))
    profile_ids = {str(profile["profile_id"]) for profile in profiles}
    if profile_ids != required_profile_ids:
        fail(
            "conformance profiles must match required fixture profile ids: "
            f"missing={sorted(required_profile_ids - profile_ids)} "
            f"stale={sorted(profile_ids - required_profile_ids)}"
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
    expected_boundary_keys = set(MANAGED_EVIDENCE_NATIVEAOT_BOUNDARY_TOKENS)
    if managed_keys != expected_boundary_keys:
        fail(
            "verified_managed implemented signals must be an explicit NativeAOT boundary set: "
            f"missing_boundary={sorted(managed_keys - expected_boundary_keys)} "
            f"stale_boundary={sorted(expected_boundary_keys - managed_keys)}"
        )

    runtime_semantics = RUNTIME_SEMANTICS_PATH.read_text()
    for key, tokens in sorted(MANAGED_EVIDENCE_NATIVEAOT_BOUNDARY_TOKENS.items()):
        for token in tokens:
            if token not in runtime_semantics:
                fail(f"{key} verified_managed boundary is not documented in RUNTIME_SEMANTICS.md: {token}")


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

    if "SqlClientNetFxIlRewriteEnabled => false" not in options:
        fail("NETFX SQLClient IL rewrite option must be recorded but remain disabled in NativeAOT mode")


def verify_semconv_attribute_contract() -> None:
    attributes = SEMCONV_ATTRIBUTES_PATH.read_text()
    generator = SEMCONV_GENERATOR_PATH.read_text()
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

    for token in [
        "QylSemConvRegistry.g.cs",
        "CollectFromNamespace",
        "IFieldSymbol",
        "HasConstantValue",
    ]:
        if token not in generator:
            fail(f"semconv registry generator missing compile-time metadata extraction token: {token}")

    for root in RUNTIME_EMISSION_ROOTS:
        for path in root.rglob("*.cs"):
            text = path.read_text()
            for pattern in FORBIDDEN_ATTRIBUTE_EMISSION_LITERAL_PATTERNS:
                match = pattern.search(text)
                if match is not None:
                    fail(f"runtime telemetry attribute emission must not use literal keys: {path.relative_to(ROOT)}")


def verify_metric_contract() -> None:
    contract = CONTRACT_PATH.read_text()
    generator = read_generator_sources()
    meters = METRIC_METERS_PATH.read_text()
    names = METRIC_NAMES_PATH.read_text()
    options = OPTIONS_PATH.read_text()
    metric_implementation_text = "\n".join([generator, meters, names])

    for token in [
        'public const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";',
        'public const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";',
        'public const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";',
        'public const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";',
        'public const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";',
        'public const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";',
        'public const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";',
        'public const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";',
        'public const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";',
        'public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";',
        'public const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";',
        'public const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";',
        'public const string NameResolutionMeterName = "System.Net.NameResolution";',
        'public const string NServiceBusIncomingPipelineMeterName = "NServiceBus.Core.Pipeline.Incoming";',
    ]:
        if token not in contract:
            fail(f"InstrumentationContract must pin the .NET 10 metric meter proof: {token}")

    for token in [
        'public const string AspNetCoreHostingMeterName = "Microsoft.AspNetCore.Hosting";',
        'public const string AspNetCoreRoutingMeterName = "Microsoft.AspNetCore.Routing";',
        'public const string AspNetCoreDiagnosticsMeterName = "Microsoft.AspNetCore.Diagnostics";',
        'public const string AspNetCoreRateLimitingMeterName = "Microsoft.AspNetCore.RateLimiting";',
        'public const string AspNetCoreHeaderParsingMeterName = "Microsoft.AspNetCore.HeaderParsing";',
        'public const string AspNetCoreServerKestrelMeterName = "Microsoft.AspNetCore.Server.Kestrel";',
        'public const string AspNetCoreHttpConnectionsMeterName = "Microsoft.AspNetCore.Http.Connections";',
        'public const string AspNetCoreAuthorizationMeterName = "Microsoft.AspNetCore.Authorization";',
        'public const string AspNetCoreAuthenticationMeterName = "Microsoft.AspNetCore.Authentication";',
        'public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";',
        'public const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";',
        'public const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";',
        'public const string NameResolutionMeterName = "System.Net.NameResolution";',
        'public const string NServiceBusIncomingPipelineMeterName = "NServiceBus.Core.Pipeline.Incoming";',
        "names.Add(AspNetCoreHostingMeterName);",
        "names.Add(AspNetCoreRoutingMeterName);",
        "names.Add(AspNetCoreDiagnosticsMeterName);",
        "names.Add(AspNetCoreRateLimitingMeterName);",
        "names.Add(AspNetCoreHeaderParsingMeterName);",
        "names.Add(AspNetCoreServerKestrelMeterName);",
        "names.Add(AspNetCoreHttpConnectionsMeterName);",
        "names.Add(AspNetCoreAuthorizationMeterName);",
        "names.Add(AspNetCoreAuthenticationMeterName);",
        "names.Add(AspNetCoreComponentsMeterName);",
        "names.Add(AspNetCoreComponentsLifecycleMeterName);",
        "names.Add(AspNetCoreComponentsServerCircuitsMeterName);",
        "names.Add(NameResolutionMeterName);",
        "names.Add(NServiceBusIncomingPipelineMeterName);",
    ]:
        if token not in meters:
            fail(f"QylMetricMeters must register expanded .NET 10 metric meters when metrics are enabled: {token}")
    if 'public const string AspNetCoreComponentsNavigate = "aspnetcore.components.navigate";' not in names:
        fail("QylMetricNames must pin the .NET 10 ASP.NET Core components navigate metric")
    if 'public const string HttpServerRequestDuration = "http.server.request.duration";' not in names:
        fail("QylMetricNames must pin the .NET 10 ASP.NET Core hosting request duration metric")
    if 'public const string DnsLookupDuration = "dns.lookup.duration";' not in names:
        fail("QylMetricNames must pin the .NET System.Net.NameResolution DNS lookup duration metric")

    for token in [
        'MetricsAdditionalSourcesVariable = "OTEL_DOTNET_AUTO_METRICS_ADDITIONAL_SOURCES"',
        "EnvironmentOptions.ReadCaseSensitiveList(MetricsAdditionalSourcesVariable)",
        "internal string[] AdditionalMetricMeterNames",
    ]:
        if token not in options:
            fail(f"QylAutoInstrumentationOptions must preserve upstream additional metric source support: {token}")

    for token in [
        "AddDistinct(names, options.AdditionalMetricMeterNames);",
        "!target.Contains(name, StringComparer.Ordinal)",
    ]:
        if token not in meters:
            fail(f"QylMetricMeters must append upstream additional metric sources without case folding or duplicates: {token}")

    for token in [
        "NavigationManager",
        "NavigateTo",
    ]:
        if token in metric_implementation_text:
            fail(f"productive code must not synthesize source-visible ASP.NET Core component metrics: {token}")


def verify_runtime_public_telemetry_status_policy() -> None:
    helper = (DIAGNOSTIC_SEMANTICS_ROOT / "ErrorStatusSemantics.cs").read_text()
    for token in [
        "ResolveHttpErrorType(ActivityKind kind, int? statusCode, string? errorType)",
        "ResolveGrpcErrorType(int? statusCode, string? errorType)",
        "SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType, errorType);",
        "activity?.SetStatus(ActivityStatusCode.Error);",
    ]:
        if token not in helper:
            fail(f"ErrorStatusSemantics must own runtime-public telemetry status token: {token}")

    for name in ["HttpSemantics.cs", "RpcSemantics.cs"]:
        text = (DIAGNOSTIC_SEMANTICS_ROOT / name).read_text()
        if "ErrorStatusSemantics.SetError(" not in text:
            fail(f"{name} must delegate error status writes to ErrorStatusSemantics")
        for token in [
            "SemanticTagWriter.Set(activity, SemanticAttributes.ErrorType",
            "SetStatus(ActivityStatusCode.Error)",
        ]:
            if token in text:
                fail(f"{name} must not write runtime-public telemetry error status directly: {token}")


def verify_runtime_public_telemetry_payload_access_policy() -> None:
    payload_readers = {
        "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/DiagnosticPayloadReader.cs": [
            "payload is IReadOnlyDictionary<string, object?> readOnlyDictionary",
            "payload is IEnumerable<KeyValuePair<string, object?>> keyValuePairs",
            "Activity.Current?.GetTagItem(key)",
        ],
        "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/AspNetCore/AspNetCorePayloadReader.cs": [
            "payload as HttpContext",
            "endpoint is RouteEndpoint routeEndpoint",
        ],
        "src/Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore/EntityFrameworkCorePayloadReader.cs": [
            "payload is not CommandEventData commandEvent",
            "payload is CommandErrorEventData errorEvent",
        ],
        "src/Qyl.OpenTelemetry.AutoInstrumentation.SqlClient/SqlClientPayloadReader.cs": [
            "payload is IEnumerable<KeyValuePair<string, object>> entries",
            "entry.Value is T matched",
            "TryGetPayloadValue<SqlCommand>(payload, CommandKey, out var sqlCommand)",
            "TryGetPayloadValue<Exception>(payload, ExceptionKey, out var exception)",
        ],
    }
    forbidden_tokens = [
        "System.Reflection",
        "GetProperty(",
        "GetProperties(",
        "GetField(",
        "GetFields(",
        "PropertyInfo",
        "FieldInfo",
        "dynamic ",
        "Activator.CreateInstance",
        "MakeGeneric",
    ]
    for relative_path, required_tokens in payload_readers.items():
        text = (ROOT / relative_path).read_text()
        for token in required_tokens:
            if token not in text:
                fail(f"runtime-public telemetry payload reader must preserve typed/public access token: {relative_path} {token}")
        for token in forbidden_tokens:
            if token in text:
                fail(f"runtime-public telemetry payload reader must not use reflection/dynamic access: {relative_path} {token}")


def verify_sensitive_attribute_emission_policy() -> None:
    policy_path = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylSensitiveCapturePolicy.cs"
    policy = policy_path.read_text()
    for token in [
        "public static void SetAspNetCoreUrlQuery(Activity activity, string query)",
        "public static void SetHttpClientUrlFull(Activity activity, string url)",
        "public static void SetDbQueryText(Activity activity, DbCommand command, string instrumentationId)",
        "public static void SetGraphQlDocument(Activity activity, string? document)",
        "QylAutoInstrumentationOptions.Current.AspNetCoreUrlQueryRedactionDisabled",
        "QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled",
        "QylCaptureHelpers.RedactQueryValues(query)",
        "QylCaptureHelpers.FormatUrlFull(",
        "QylAutoInstrumentationOptions.Current.GraphQlSetDocument",
        "activity.SetTag(QylSemanticAttributes.DbQueryText, command.CommandText);",
        "activity.SetTag(QylSemanticAttributes.GraphQlDocument, document);",
    ]:
        if token not in policy:
            fail(f"QylSensitiveCapturePolicy must own sensitive raw attribute token: {token}")

    http_semantics_path = ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners" / "Semantics" / "HttpSemantics.cs"
    http_semantics = http_semantics_path.read_text()
    for token in [
        "public static void SetUrlTags(Activity? activity, string? url, string? serverAddress, int? serverPort)",
        "SemanticTagWriter.Set(",
        "SemanticAttributes.UrlFull",
        "QylCaptureHelpers.FormatUrlFull(",
        "QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled",
    ]:
        if token not in http_semantics:
            fail(f"HttpSemantics must own runtime-public url.full redaction token: {token}")

    allowed_raw_settag_paths = {
        "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylSensitiveCapturePolicy.cs",
    }
    allowed_url_format_paths = {
        "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylCaptureHelpers.cs",
        "src/Qyl.OpenTelemetry.AutoInstrumentation/Internal/QylSensitiveCapturePolicy.cs",
        "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Semantics/HttpSemantics.cs",
    }
    allowed_db_query_text_paths = {
        "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/EntityFrameworkCore/EntityFrameworkCoreDiagnosticListener.cs",
        "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/SqlClient/SqlClientDiagnosticListener.cs",
        "src/Qyl.OpenTelemetry.AutoInstrumentation.EntityFrameworkCore/EntityFrameworkCoreDiagnosticListener.cs",
        "src/Qyl.OpenTelemetry.AutoInstrumentation.SqlClient/SqlClientDiagnosticListener.cs",
    }

    for root in RUNTIME_EMISSION_ROOTS:
        for path in root.rglob("*.cs"):
            relative_path = path.relative_to(ROOT).as_posix()
            text = path.read_text()

            for token in [
                "SetTag(QylSemanticAttributes.UrlQuery",
                "SetTag(QylSemanticAttributes.UrlFull",
                "SetTag(QylSemanticAttributes.DbQueryText",
                "SetTag(QylSemanticAttributes.GraphQlDocument",
            ]:
                if token in text and relative_path not in allowed_raw_settag_paths:
                    fail(f"sensitive raw attribute writes must go through QylSensitiveCapturePolicy: {relative_path} {token}")

            for token in [
                "SemanticTagWriter.Set(activity, SemanticAttributes.UrlFull",
                "SemanticTagWriter.Set(activity, SemanticAttributes.UrlQuery",
                "SemanticTagWriter.Set(activity, SemanticAttributes.GraphQlDocument",
            ]:
                if token in text and relative_path != "src/Qyl.OpenTelemetry.AutoInstrumentation.DiagnosticListeners/Semantics/HttpSemantics.cs":
                    fail(f"runtime-public sensitive writes must go through the owning semantics helper: {relative_path} {token}")

            if "QylCaptureHelpers.FormatUrlFull(" in text and relative_path not in allowed_url_format_paths:
                fail(f"url.full formatting must stay centralized behind sensitive capture policy/HttpSemantics: {relative_path}")

            if "QylCaptureHelpers.RedactQueryValues(" in text and relative_path not in allowed_url_format_paths:
                fail(f"url query redaction must stay centralized behind sensitive capture policy/helpers: {relative_path}")

            if "SemanticTagWriter.Set(activity, SemanticAttributes.DbQueryText" not in text:
                continue

            if relative_path not in allowed_db_query_text_paths:
                fail(f"db.query.text writes must be owned by typed DB listener paths: {relative_path}")

            for match in re.finditer(r"SemanticTagWriter\.Set\(activity,\s*SemanticAttributes\.DbQueryText", text):
                guard_window = text[max(0, match.start() - 260):match.start()]
                if "if (DatabaseSemantics.ShouldWriteQueryText(" not in guard_window:
                    fail(f"db.query.text write must be guarded by DatabaseSemantics.ShouldWriteQueryText: {relative_path}")


def verify_bounded_activity_name_policy() -> None:
    names = ACTIVITY_NAMES_PATH.read_text()
    for token in [
        "Composes the bounded, low-cardinality span names emitted by qyl auto-instrumentation.",
        "Every input is already low-cardinality by construction",
        "private const string HttpFallback = \"HTTP\";",
        "private const string GrpcFallback = \"gRPC\";",
        "private const string DbFallback = \"DB CLIENT\";",
        "private const string SqlFallback = \"SQL CLIENT\";",
        "internal const string KafkaMessage = \"Kafka message\";",
        "internal const string MongoDbCommand = \"MongoDB command\";",
        "internal const string RabbitMqPublish = \"RabbitMQ publish\";",
        "public static string HttpClient(string? normalizedMethod)",
        "public static string HttpServer(string? normalizedMethod, string? route)",
        "public static string GrpcClient(string? service, string? method)",
        "public static string DbCommand(string? operation)",
        "public static string SqlClientCommand(string? operation)",
    ]:
        if token not in names:
            fail(f"QylActivityNames must preserve bounded span-name policy token: {token}")

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


def parse_interceptor_emitter_blocks(generator: str) -> dict[str, str]:
    blocks: dict[str, str] = {}
    for match in re.finditer(r"\n    private static void (Emit[A-Za-z0-9]+Interceptor)\(", generator):
        name = match.group(1)
        brace_index = generator.find("{", match.end())
        if brace_index < 0:
            fail(f"{name} has no method body")

        depth = 0
        for index in range(brace_index, len(generator)):
            char = generator[index]
            if char == "{":
                depth += 1
            elif char == "}":
                depth -= 1
                if depth == 0:
                    blocks[name] = generator[brace_index:index + 1]
                    break
        else:
            fail(f"{name} has unterminated method body")

    return blocks


def verify_interceptor_emitter_runtime_delegation(generator: str) -> None:
    emitter_blocks = parse_interceptor_emitter_blocks(generator)
    if not emitter_blocks:
        fail("generator has no Emit*Interceptor methods")

    descriptor_delegation_tokens = [
        "descriptor.HelperType",
        "descriptor.AppendStartActivity(builder, in target)",
        "descriptor.GetRecordExceptionStatement()",
        "descriptor.ObserveAsyncMethod",
        "EmitDirectLoggerInterceptor(",
        "EmitLoggerExtensionInterceptor(",
    ]
    for name, body in sorted(emitter_blocks.items()):
        if name == "EmitMeterProviderBuilderAddMeterInterceptor":
            if REQUIRED_METER_PROVIDER_DELEGATION_TOKEN not in body and "descriptor.EnabledMeterNamesExpression" not in body:
                fail(f"{name} must delegate meter registration to QylMetricMeters")
            continue

        if REQUIRED_INTERCEPTOR_EMITTER_DELEGATION_TOKEN not in body and not any(token in body for token in descriptor_delegation_tokens):
            fail(f"{name} must delegate intercepted behavior to Qyl runtime instrumentation")

    for name in [
        "EmitDirectLoggerInterceptor",
        "EmitLoggerExtensionInterceptor",
        "EmitExternalLoggerInterceptor",
    ]:
        body = emitter_blocks.get(name)
        if body is None:
            fail(f"{name} missing from generator")
        if "descriptor.HelperType" not in body:
            fail(f"{name} must route logging helper calls through its body descriptor")
        for token in [
            "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedLogger.",
            "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedExternalLogger.",
        ]:
            if token in body:
                fail(f"{name} must not hardcode logging runtime helper calls: {token}")

    wrapper_match = re.search(
        r"private static void EmitGrpcStreamReaderWrapper\(StringBuilder builder, string helperType\).*?\n    }",
        generator,
        re.DOTALL,
    )
    if wrapper_match is None:
        fail("EmitGrpcStreamReaderWrapper missing from generator")
    grpc_wrapper = wrapper_match.group(0)
    for token in [
        "EmitGrpcStreamReaderWrapper(StringBuilder builder, string helperType)",
        "builder.Append(helperType);",
        "GetGrpcStreamReaderHelperType(invocations)",
    ]:
        if token not in generator:
            fail(f"gRPC stream wrapper must route helper calls through GrpcClientBodyDescriptor.HelperType: {token}")
    if "global::Qyl.OpenTelemetry.AutoInstrumentation.QylInterceptedGrpcNetClient." in grpc_wrapper:
        fail("gRPC stream wrapper must not hardcode QylInterceptedGrpcNetClient helper calls")

    specialized_descriptor_methods = {
        "EmitHttpWebRequestInterceptor": [
            "descriptor.GetStartTimeUtcMethod",
            "descriptor.StartActivityMethod",
            "descriptor.RecordResultMethod",
            "descriptor.RecordExceptionMethod",
        ],
        "EmitDbCommandInterceptor": [
            "descriptor.GetTimestampMethod",
            "descriptor.StartActivityMethod",
            "descriptor.ObserveAsyncMethod",
            "descriptor.RecordExceptionMethod",
            "descriptor.RecordDurationMethod",
        ],
    }
    for name, required_tokens in sorted(specialized_descriptor_methods.items()):
        body = emitter_blocks.get(name)
        if body is None:
            fail(f"{name} missing from generator")
        for token in required_tokens:
            if token not in body:
                fail(f"{name} must route specialized runtime method names through its body descriptor: {token}")

    specialized_forbidden_tokens = {
        "EmitHttpWebRequestInterceptor": [
            ".GetStartTimeUtc();",
            ".StartActivity(httpWebRequest, ",
            ".RecordResult(activity, metricStartTimeUtc, httpWebRequest.Method, result);",
            ".RecordException(activity, metricStartTimeUtc, httpWebRequest.Method, exception);",
        ],
        "EmitDbCommandInterceptor": [
            ".GetTimestamp();",
            ".StartActivity(",
            ".ObserveAsync(resultTask, activity, metricStart, ",
            ".RecordException(activity, exception);",
            ".RecordDuration(metricStart, ",
        ],
    }
    for name, forbidden_tokens in sorted(specialized_forbidden_tokens.items()):
        body = emitter_blocks[name]
        for token in forbidden_tokens:
            if token in body:
                fail(f"{name} must not hardcode specialized runtime helper method name: {token}")


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


def verify_intercepted_runtime_error_policy() -> None:
    helper = ACTIVITY_STATUS_PATH.read_text()
    for token in [
        "activity.SetTag(QylSemanticAttributes.ErrorType, exception.GetType().Name);",
        "activity.SetTag(QylSemanticAttributes.ErrorType, statusCode.ToString(CultureInfo.InvariantCulture));",
        "activity.SetStatus(ActivityStatusCode.Error);",
    ]:
        if token not in helper:
            fail(f"QylActivityStatus must own intercepted runtime error policy token: {token}")

    for path in sorted((ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation").glob("QylIntercepted*.cs")):
        text = path.read_text()
        for token in [
            "SetTag(QylSemanticAttributes.ErrorType",
            "SetStatus(ActivityStatusCode.Error)",
        ]:
            if token in text:
                fail(f"intercepted runtime helper must delegate error policy to QylActivityStatus: {path.relative_to(ROOT)} {token}")


def verify_intercepted_runtime_activity_start_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylActivityFactory.cs").read_text()
    for token in [
        "public static Activity? StartTraceActivity(",
        "public static Activity? StartLogActivity(",
        "QylAutoInstrumentationOptions.Current.IsInstrumentationEnabled(signal, instrumentationId)",
        "QylActivitySource.StartActivity(activityName, activityKind)",
        "activity.SetTag(QylSemanticAttributes.QylInstrumentationDomain, instrumentationDomain);",
    ]:
        if token not in helper:
            fail(f"QylActivityFactory must own intercepted activity start policy token: {token}")

    factory_owned_helpers = {
        "QylInterceptedAzure.cs",
        "QylInterceptedElastic.cs",
        "QylInterceptedExternalLogger.cs",
        "QylInterceptedGraphQl.cs",
        "QylInterceptedKafka.cs",
        "QylInterceptedLogger.cs",
        "QylInterceptedMassTransit.cs",
        "QylInterceptedEntityFrameworkCore.cs",
        "QylInterceptedGrpcNetClient.cs",
        "QylInterceptedMongoDb.cs",
        "QylInterceptedNServiceBus.cs",
        "QylInterceptedQuartz.cs",
        "QylInterceptedRabbitMq.cs",
        "QylInterceptedRedis.cs",
        "QylInterceptedWcfClient.cs",
        "QylInterceptedWcfCore.cs",
    }
    for name in sorted(factory_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in [
            "QylActivitySource.StartActivity(",
            "SetTag(QylSemanticAttributes.QylInstrumentationDomain",
        ]:
            if token in text:
                fail(f"intercepted runtime helper must delegate start/domain policy to QylActivityFactory: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_messaging_activity_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylMessagingActivityPolicy.cs").read_text()
    for token in [
        "QylActivityFactory.StartTraceActivity(",
        "QylActivityTags.SetMessaging(activity, messagingSystem, operationType, operationName);",
        "QylAutoInstrumentationIds.Kafka",
        "QylAutoInstrumentationIds.MassTransit",
        "QylAutoInstrumentationIds.NServiceBus",
        "QylAutoInstrumentationIds.RabbitMq",
        "ActivityKind.Consumer",
        "ActivityKind.Producer",
        "QylSemanticAttributes.MessagingOperationTypeReceive",
        "QylSemanticAttributes.MessagingOperationTypeSend",
        "NormalizeSendPublishOperation",
    ]:
        if token not in helper:
            fail(f"QylMessagingActivityPolicy must own messaging activity token: {token}")

    messaging_policy_owned_helpers = {
        "QylInterceptedKafka.cs",
        "QylInterceptedMassTransit.cs",
        "QylInterceptedNServiceBus.cs",
        "QylInterceptedRabbitMq.cs",
    }
    forbidden_tokens = [
        "QylActivityFactory.StartTraceActivity(",
        "QylActivityTags.SetMessaging(",
        "ActivityKind.Consumer",
        "ActivityKind.Producer",
        "MessagingSystemKafka",
        "MessagingSystemMassTransit",
        "MessagingSystemNServiceBus",
        "MessagingSystemRabbitMq",
        "MessagingOperationTypeSend",
        "MessagingOperationTypeReceive",
        "MessagingOperationNamePublish",
        "MessagingOperationNameSend",
    ]
    for name in sorted(messaging_policy_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate messaging activity policy to QylMessagingActivityPolicy: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_db_activity_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylDbActivityPolicy.cs").read_text()
    for token in [
        "QylActivityFactory.StartTraceActivity(",
        "QylActivityNames.DbCommand(operation)",
        "QylInstrumentationDomains.DbClient",
        "QylActivityTags.SetDb(",
        "QylActivityTags.SetDbOperation(",
        "QylAutoInstrumentationIds.EntityFrameworkCore",
        "QylInstrumentationDomains.DbEfCore",
        "StartEntityFrameworkCoreActivity",
        "activity.SetTag(QylSemanticAttributes.DbNamespace, databaseName);",
        "QylSensitiveCapturePolicy.SetDbQueryText(activity, command, instrumentationId);",
        "command.CommandType is CommandType.StoredProcedure",
        "FirstToken(text)",
        "IsKnownDbOperation(token)",
        "QylAutoInstrumentationIds.SqlClient => QylSemanticAttributes.DbSystemMicrosoftSqlServer",
        "QylAutoInstrumentationIds.Npgsql => QylSemanticAttributes.DbSystemPostgresql",
    ]:
        if token not in helper:
            fail(f"QylDbActivityPolicy must own DbCommand activity token: {token}")

    db_activity_policy_owned_helpers = {
        "QylInterceptedDbCommand.cs",
        "QylInterceptedEntityFrameworkCore.cs",
    }
    forbidden_tokens = [
        "QylActivitySource.StartActivity(",
        "QylActivityNames.DbCommand(",
        "QylInstrumentationDomains.DbClient",
        "QylInstrumentationDomains.DbEfCore",
        "SetTag(QylSemanticAttributes.DbNamespace",
        "SetTag(QylSemanticAttributes.DbOperationName",
        "SetTag(QylSemanticAttributes.DbQuerySummary",
        "CommandType.StoredProcedure",
        "FirstToken(",
        "IsKnownDbOperation(",
        "GetDbSystemName(",
        "NormalizeOperation(",
        "GetQuerySummary(",
    ]
    for name in sorted(db_activity_policy_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate DB activity policy to QylDbActivityPolicy: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_http_activity_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylHttpActivityPolicy.cs").read_text()
    for token in [
        "QylActivityFactory.StartTraceActivity(",
        "QylActivityNames.HttpClient(method)",
        "QylActivityNames.HttpServer(method, route)",
        "activity.SetTag(QylSemanticAttributes.HttpRequestMethod, method);",
        "activity.SetTag(QylSemanticAttributes.ServerAddress, requestUri.Host);",
        "activity.SetTag(QylSemanticAttributes.ServerPort, requestUri.Port);",
        "QylSensitiveCapturePolicy.SetHttpClientUrlFull(activity, urlFull);",
        "activity.SetTag(QylSemanticAttributes.UrlPath, path);",
        "QylSensitiveCapturePolicy.SetAspNetCoreUrlQuery(activity, query);",
        "activity.SetTag(QylSemanticAttributes.HttpRoute, route);",
        "activity.SetTag(QylSemanticAttributes.HttpResponseStatusCode, statusCode);",
        "QylActivityStatus.RecordError(activity, statusCode);",
    ]:
        if token not in helper:
            fail(f"QylHttpActivityPolicy must own HTTP activity token: {token}")

    http_policy_owned_helpers = {
        "QylInterceptedAspNetCore.cs",
        "QylInterceptedHttpClient.cs",
        "QylInterceptedHttpWebRequest.cs",
    }
    forbidden_tokens = [
        "QylActivityNames.HttpClient(",
        "QylActivityNames.HttpServer(",
        "SetTag(QylSemanticAttributes.HttpRequestMethod",
        "SetTag(QylSemanticAttributes.HttpResponseStatusCode",
        "SetTag(QylSemanticAttributes.ServerAddress",
        "SetTag(QylSemanticAttributes.ServerPort",
        "SetTag(QylSemanticAttributes.UrlPath",
        "SetTag(QylSemanticAttributes.HttpRoute",
        "QylActivityStatus.RecordError(activity, statusCode)",
        "QylActivityStatus.RecordError(activity, context.Response.StatusCode)",
    ]
    for name in sorted(http_policy_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate HTTP activity policy to QylHttpActivityPolicy: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_rpc_activity_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylRpcActivityPolicy.cs").read_text()
    for token in [
        "QylActivityFactory.StartTraceActivity(",
        "QylAutoInstrumentationIds.GrpcNetClient",
        "QylActivityNames.GrpcClient(service, methodName)",
        "QylInstrumentationDomains.RpcGrpc",
        "QylSemanticAttributes.RpcSystemGrpc",
        "QylAutoInstrumentationIds.WcfClient",
        "QylActivityNames.WcfClient",
        "QylInstrumentationDomains.RpcWcfClient",
        "QylAutoInstrumentationIds.WcfCore",
        "QylActivityNames.CoreWcfServer",
        "QylInstrumentationDomains.RpcWcfCore",
        "QylSemanticAttributes.RpcSystemDotNetWcf",
        "QylActivityTags.SetRpc(",
        "QylCaptureHelpers.SetMetadataHeaders(",
        "QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedRequestMetadataMap",
        "QylAutoInstrumentationOptions.Current.GrpcNetClientCapturedResponseMetadataMap",
        "activity.SetTag(QylSemanticAttributes.RpcGrpcStatusCode, QylSemanticAttributes.RpcGrpcStatusCodeOk);",
        "GetGrpcServiceName",
    ]:
        if token not in helper:
            fail(f"QylRpcActivityPolicy must own RPC activity token: {token}")

    rpc_policy_owned_helpers = {
        "QylInterceptedGrpcNetClient.cs",
        "QylInterceptedWcfClient.cs",
        "QylInterceptedWcfCore.cs",
    }
    forbidden_tokens = [
        "QylActivitySource.StartActivity(",
        "QylActivityFactory.StartTraceActivity(",
        "QylActivityTags.SetRpc(",
        "SetTag(QylSemanticAttributes.RpcSystem",
        "SetTag(QylSemanticAttributes.RpcService",
        "SetTag(QylSemanticAttributes.RpcMethod",
        "SetTag(QylSemanticAttributes.RpcGrpcStatusCode",
        "QylCaptureHelpers.SetMetadataHeaders(",
        "QylActivityNames.GrpcClient(",
        "QylActivityNames.WcfClient",
        "QylActivityNames.CoreWcfServer",
        "QylInstrumentationDomains.RpcGrpc",
        "QylInstrumentationDomains.RpcWcfClient",
        "QylInstrumentationDomains.RpcWcfCore",
        "QylSemanticAttributes.RpcSystemGrpc",
        "QylSemanticAttributes.RpcSystemDotNetWcf",
        "GrpcNetClientCapturedRequestMetadataMap",
        "GrpcNetClientCapturedResponseMetadataMap",
    ]
    for name in sorted(rpc_policy_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate RPC activity policy to QylRpcActivityPolicy: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_sensitive_capture_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylSensitiveCapturePolicy.cs").read_text()
    for token in [
        "QylAutoInstrumentationOptions.Current.AspNetCoreUrlQueryRedactionDisabled",
        "QylCaptureHelpers.RedactQueryValues(query)",
        "QylAutoInstrumentationOptions.Current.HttpClientUrlQueryRedactionDisabled",
        "QylCaptureHelpers.FormatUrlFull(",
        "activity.SetTag(QylSemanticAttributes.DbQueryText, command.CommandText);",
        "QylAutoInstrumentationIds.SqlClient => options.SqlClientSetDbStatementForText",
        "QylAutoInstrumentationIds.EntityFrameworkCore => options.EntityFrameworkCoreSetDbStatementForText",
        "QylAutoInstrumentationIds.OracleMda => options.OracleMdaSetDbStatementForText",
        "QylAutoInstrumentationOptions.Current.GraphQlSetDocument",
        "activity.SetTag(QylSemanticAttributes.GraphQlDocument, document);",
    ]:
        if token not in helper:
            fail(f"QylSensitiveCapturePolicy must own sensitive capture token: {token}")

    sensitive_capture_owned_helpers = {
        "QylInterceptedAspNetCore.cs",
        "QylInterceptedDbCommand.cs",
        "QylInterceptedGraphQl.cs",
        "QylInterceptedHttpClient.cs",
        "QylInterceptedHttpWebRequest.cs",
    }
    forbidden_tokens = [
        "AspNetCoreUrlQueryRedactionDisabled",
        "HttpClientUrlQueryRedactionDisabled",
        "GraphQlSetDocument",
        "SqlClientSetDbStatementForText",
        "EntityFrameworkCoreSetDbStatementForText",
        "OracleMdaSetDbStatementForText",
        "SetTag(QylSemanticAttributes.DbQueryText",
        "SetTag(QylSemanticAttributes.GraphQlDocument",
        "QylCaptureHelpers.FormatUrlFull",
        "QylCaptureHelpers.RedactQueryValues",
    ]
    for name in sorted(sensitive_capture_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate sensitive capture to QylSensitiveCapturePolicy: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_duration_metric_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylDurationMetrics.cs").read_text()
    for token in [
        "QylHttpClientMetrics.RecordRequestDuration(startTimeUtc, method, statusCode);",
        "QylHttpClientMetrics.RecordRequestDurationUnchecked(startTimeUtc, method, statusCode);",
        "QylDbClientMetrics.GetTimestamp();",
        "QylDbClientMetrics.RecordDuration(startTimestamp, instrumentationId);",
        "QylNServiceBusMetrics.GetTimestamp();",
        "QylNServiceBusMetrics.RecordDuration(startTimestamp, operationName);",
    ]:
        if token not in helper:
            fail(f"QylDurationMetrics must own duration metric token: {token}")

    generator = read_generator_sources()
    for token in [
        "QylDbClientMetrics.GetTimestamp",
        "QylDbClientMetrics.RecordDuration",
        "QylNServiceBusMetrics.GetTimestamp",
        "QylNServiceBusMetrics.RecordDuration",
    ]:
        if token in generator:
            fail(f"source generator must delegate duration metrics through QylIntercepted runtime helpers: {token}")

    duration_owned_helpers = {
        "QylInterceptedDbCommand.cs",
        "QylInterceptedHttpClient.cs",
        "QylInterceptedHttpWebRequest.cs",
        "QylInterceptedNServiceBus.cs",
    }
    forbidden_tokens = [
        "QylHttpClientMetrics.RecordRequestDuration",
        "QylDbClientMetrics.GetTimestamp",
        "QylDbClientMetrics.RecordDuration",
        "QylDbClientMetrics.IsRecordingEnabled",
        "QylNServiceBusMetrics.GetTimestamp",
        "QylNServiceBusMetrics.RecordDuration",
    ]
    for name in sorted(duration_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate duration metrics to QylDurationMetrics: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_semantic_tag_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylActivityTags.cs").read_text()
    for token in [
        "activity.SetTag(QylSemanticAttributes.MessagingSystem, system);",
        "activity.SetTag(QylSemanticAttributes.MessagingOperationType, operationType);",
        "activity.SetTag(QylSemanticAttributes.MessagingOperationName, operationName);",
        "activity.SetTag(QylSemanticAttributes.DbSystemName, systemName);",
        "activity.SetTag(QylSemanticAttributes.DbOperationName, operationName);",
        "activity.SetTag(QylSemanticAttributes.DbQuerySummary, querySummary);",
        "activity.SetTag(QylSemanticAttributes.RpcSystem, system);",
        "activity.SetTag(QylSemanticAttributes.RpcService, service);",
        "activity.SetTag(QylSemanticAttributes.RpcMethod, method);",
        "activity.SetTag(QylSemanticAttributes.GraphQlOperationName, operationName);",
        "activity.SetTag(QylSemanticAttributes.LogSeverity, severity);",
    ]:
        if token not in helper:
            fail(f"QylActivityTags must own semantic tag-set token: {token}")

    tag_set_owned_helpers = {
        "QylInterceptedElastic.cs",
        "QylInterceptedExternalLogger.cs",
        "QylInterceptedGraphQl.cs",
        "QylInterceptedGrpcNetClient.cs",
        "QylInterceptedKafka.cs",
        "QylInterceptedLogger.cs",
        "QylInterceptedMassTransit.cs",
        "QylInterceptedEntityFrameworkCore.cs",
        "QylInterceptedMongoDb.cs",
        "QylInterceptedNServiceBus.cs",
        "QylInterceptedRabbitMq.cs",
        "QylInterceptedRedis.cs",
        "QylInterceptedWcfClient.cs",
        "QylInterceptedWcfCore.cs",
    }
    forbidden_tokens = [
        "SetTag(QylSemanticAttributes.MessagingSystem",
        "SetTag(QylSemanticAttributes.MessagingOperationType",
        "SetTag(QylSemanticAttributes.MessagingOperationName",
        "SetTag(QylSemanticAttributes.DbSystemName",
        "SetTag(QylSemanticAttributes.DbOperationName",
        "SetTag(QylSemanticAttributes.DbQuerySummary",
        "SetTag(QylSemanticAttributes.RpcSystem",
        "SetTag(QylSemanticAttributes.RpcService",
        "SetTag(QylSemanticAttributes.RpcMethod",
        "SetTag(QylSemanticAttributes.GraphQlOperationName",
        "SetTag(QylSemanticAttributes.LogSeverity",
    ]
    for name in sorted(tag_set_owned_helpers):
        text = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / name).read_text()
        for token in forbidden_tokens:
            if token in text:
                fail(f"intercepted runtime helper must delegate semantic tag sets to QylActivityTags: src/Qyl.OpenTelemetry.AutoInstrumentation/{name} {token}")


def verify_intercepted_runtime_async_observer_policy() -> None:
    helper = (ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation" / "Internal" / "QylActivityObserver.cs").read_text()
    for token in [
        "public static Task ObserveAsync(Task? task, Activity? activity)",
        "public static Task<T> ObserveAsync<T>(Task<T>? task, Activity? activity)",
        "QylActivityStatus.RecordException(activity, exception);",
        "activity.Dispose();",
    ]:
        if token not in helper:
            fail(f"QylActivityObserver must own async activity observation token: {token}")

    allowed_local_observers = {
        "QylInterceptedDbCommand.cs",
    }
    for path in sorted((ROOT / "src" / "Qyl.OpenTelemetry.AutoInstrumentation").glob("QylIntercepted*.cs")):
        text = path.read_text()
        if "ObserveSlowAsync" in text and path.name not in allowed_local_observers:
            fail(f"intercepted runtime helper must delegate async observation to QylActivityObserver: {path.relative_to(ROOT)}")


def verify_behavior_semantics_contract() -> None:
    generator = read_generator_sources()
    if "global::Qyl.OpenTelemetry.AutoInstrumentation.QylIntercepted" not in generator:
        fail("generator must delegate intercepted call-sites to the Qyl runtime instrumentation assembly")
    verify_interceptor_emitter_runtime_delegation(generator)
    verify_intercepted_runtime_error_policy()
    verify_intercepted_runtime_activity_start_policy()
    verify_intercepted_runtime_messaging_activity_policy()
    verify_intercepted_runtime_db_activity_policy()
    verify_intercepted_runtime_http_activity_policy()
    verify_intercepted_runtime_rpc_activity_policy()
    verify_intercepted_runtime_sensitive_capture_policy()
    verify_intercepted_runtime_duration_metric_policy()
    verify_intercepted_runtime_semantic_tag_policy()
    verify_intercepted_runtime_async_observer_policy()

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
            for token in FORBIDDEN_PRODUCTIVE_MECHANISM_TOKENS:
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


def parse_emission_descriptor_ownership(generator: str) -> dict[str, str]:
    ownership_by_kind: dict[str, str] = {}
    for match in re.finditer(
        r"new\s+InterceptorEmissionDescriptor\(\s*InterceptorKind\.([A-Za-z0-9]+).*?InterceptorSignalOwnership\.([A-Za-z0-9]+)",
        generator,
        re.DOTALL,
    ):
        kind = match.group(1)
        ownership = match.group(2)
        if kind in ownership_by_kind:
            fail(f"duplicate emission descriptor ownership for InterceptorKind.{kind}")
        ownership_by_kind[kind] = ownership

    if not ownership_by_kind:
        fail("s_emissionDescriptors must declare signal ownership")

    return ownership_by_kind


def parse_emission_descriptor_policies(generator: str) -> dict[str, tuple[str, str, str, str, str]]:
    policies_by_kind: dict[str, tuple[str, str, str, str, str]] = {}
    for line in generator.splitlines():
        if "new InterceptorEmissionDescriptor(" not in line:
            continue

        match = re.search(
            r"InterceptorKind\.([A-Za-z0-9]+).*?"
            r"InterceptorMethodShape\.([A-Za-z0-9]+),\s*"
            r"InterceptorSignalOwnership\.([A-Za-z0-9]+),\s*"
            r"InterceptorErrorPolicy\.([A-Za-z0-9]+),\s*"
            r"InterceptorDurationPolicy\.([A-Za-z0-9]+)",
            line,
        )
        if match is None:
            fail(f"emission descriptor missing signal/error/duration policies: {line.strip()}")

        body_tokens = [
            "TraceBody",
            "ForwardingBody",
            "HttpWebRequestBody",
            "DbCommandBody",
            "GrpcClientBody",
            "MeterProviderBuilderBody",
            "LoggerBody",
            "ExternalLoggerBody",
        ]
        body_matches = [token for token in body_tokens if re.search(rf"\b{token}:", line)]
        if len(body_matches) != 1:
            fail(f"emission descriptor must declare exactly one typed body descriptor: {line.strip()}")

        kind = match.group(1)
        if kind in policies_by_kind:
            fail(f"duplicate emission descriptor policy for InterceptorKind.{kind}")
        policies_by_kind[kind] = (match.group(2), match.group(3), match.group(4), match.group(5), body_matches[0])

    if not policies_by_kind:
        fail("s_emissionDescriptors must declare signal/error/duration policies")

    return policies_by_kind


def parse_matcher_descriptor_contract_keys(generator: str) -> set[str]:
    matcher_keys = set()
    for block in extract_parenthesized_blocks(generator, "new InterceptorMatcherDescriptor"):
        matcher_keys.update(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', block))

    if not matcher_keys:
        fail("s_matcherDescriptors must describe source-visible matcher contract keys")

    return matcher_keys


def parse_matcher_descriptor_kinds(generator: str) -> set[str]:
    descriptor_lines = [
        line.strip()
        for line in generator.splitlines()
        if "new InterceptorMatcherDescriptor(" in line
    ]
    if not descriptor_lines:
        fail("s_matcherDescriptors must declare source-visible matcher descriptors")

    matcher_kinds: set[str] = set()
    for line in descriptor_lines:
        declared_kinds = set(re.findall(r"InterceptorKind\.([A-Za-z0-9]+)", line))
        if not declared_kinds:
            fail(f"matcher descriptor must declare target InterceptorKind values: {line}")
        matcher_kinds.update(declared_kinds)

    return matcher_kinds


def parse_contract_keys_call_keys(generator: str) -> set[str]:
    keys: set[str] = set()
    for match in re.finditer(r"ContractKeys\((.*?)\)", generator, re.DOTALL):
        keys.update(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', match.group(1)))

    return keys


def parse_switch_helper_keys(generator: str, helper_name: str) -> set[str]:
    match = re.search(
        rf"private static [^=]+ {re.escape(helper_name)}\([^)]*\)\s*=>.*?}};",
        generator,
        re.DOTALL,
    )
    if match is None:
        fail(f"{helper_name} helper missing from generator")

    return set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', match.group(0)))


def extract_parenthesized_blocks(text: str, token: str) -> list[str]:
    blocks: list[str] = []
    position = 0
    while True:
        token_index = text.find(token, position)
        if token_index < 0:
            return blocks

        open_index = text.find("(", token_index + len(token))
        if open_index < 0:
            return blocks

        depth = 0
        for index in range(open_index, len(text)):
            char = text[index]
            if char == "(":
                depth += 1
            elif char == ")":
                depth -= 1
                if depth == 0:
                    blocks.append(text[open_index + 1:index])
                    position = index + 1
                    break
        else:
            fail(f"unterminated parenthesized block after {token}")


def collect_generator_target_contract_keys_by_kind(generator: str) -> dict[str, set[str]]:
    keys_by_kind: dict[str, set[str]] = {}
    for block in [
        *extract_parenthesized_blocks(generator, "new InterceptorTarget"),
        *extract_parenthesized_blocks(generator, "=> new"),
    ]:
        kind_match = re.search(r"InterceptorKind\.([A-Za-z0-9]+)", block)
        if kind_match is None:
            continue

        kind = kind_match.group(1)
        keys = set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', block))
        if "GetDbTraceContractKey(instrumentationId)" in block:
            keys.update(parse_switch_helper_keys(generator, "GetDbTraceContractKey"))
        if "GetDbMetricContractKeys(instrumentationId)" in block:
            keys.update(parse_switch_helper_keys(generator, "GetDbMetricContractKeys"))
        if keys:
            keys_by_kind.setdefault(kind, set()).update(keys)

    for method_match in re.finditer(
        r"private static bool [^{]+\{(?P<body>.*?target\s*=\s*new\s+InterceptorTarget\(\s*kind\s*,\s*\"(?P<key>signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)\".*?return true;)",
        generator,
        re.DOTALL,
    ):
        key = method_match.group("key")
        for kind in re.findall(r"kind\s*=\s*InterceptorKind\.([A-Za-z0-9]+)", method_match.group("body")):
            keys_by_kind.setdefault(kind, set()).add(key)

    return keys_by_kind


def verify_interceptor_signal_ownership(generator: str, kinds: set[str]) -> None:
    ownership_by_kind = parse_emission_descriptor_ownership(generator)
    target_keys_by_kind = collect_generator_target_contract_keys_by_kind(generator)
    missing_ownership = kinds - set(ownership_by_kind)
    if missing_ownership:
        fail(f"InterceptorKind values missing signal ownership descriptors: {sorted(missing_ownership)}")

    missing_target_keys = set(ownership_by_kind) - set(target_keys_by_kind)
    if missing_target_keys:
        fail(f"InterceptorKind values missing target contract keys for ownership verification: {sorted(missing_target_keys)}")

    for kind, ownership in sorted(ownership_by_kind.items()):
        keys = target_keys_by_kind.get(kind, set())
        has_trace = any(key.startswith("signals.traces.") for key in keys)
        has_metric = any(key.startswith("signals.metrics.") for key in keys)
        has_log = any(key.startswith("signals.logs.") for key in keys)
        allowed = {
            "TraceAndMetric" if has_trace and has_metric and not has_log else "",
            "Trace" if has_trace and not has_metric and not has_log else "",
            "Metric" if has_metric and not has_trace and not has_log else "",
            "Log" if has_log and not has_trace and not has_metric else "",
        }
        allowed.discard("")
        if ownership not in allowed:
            fail(
                f"InterceptorKind.{kind} ownership {ownership} does not match target contract keys "
                f"{sorted(keys)}; expected one of {sorted(allowed)}"
            )


def verify_interceptor_policy_shapes(generator: str, kinds: set[str]) -> None:
    for token in [
        "ValidateMethodShape(in descriptor);",
        "ValidateSingleBodyDescriptor(in descriptor);",
        "Interceptor emission descriptor must define exactly one typed body descriptor",
        "Interceptor emission descriptor method shape mismatch",
        "Trace body descriptor has unsupported method shape",
        "Trace body descriptor must provide a runtime helper",
        "Forwarding body descriptor has unsupported method shape",
        "ValidateEmissionDescriptorPolicy(in descriptor);",
        "Runtime metric duration policy requires trace+metric ownership",
        "Trace+metric ownership requires runtime metric duration policy",
        "Trace runtime metric descriptor must provide a duration metric descriptor",
        "Unsupported interceptor emission policy shape",
    ]:
        if token not in generator:
            fail(f"generator must validate descriptor signal/error/duration policy token: {token}")

    policies_by_kind = parse_emission_descriptor_policies(generator)
    missing_policies = kinds - set(policies_by_kind)
    if missing_policies:
        fail(f"InterceptorKind values missing signal/error/duration policies: {sorted(missing_policies)}")

    for body_name in [
        "TraceBody",
        "ForwardingBody",
        "HttpWebRequestBody",
        "DbCommandBody",
        "GrpcClientBody",
        "MeterProviderBuilderBody",
        "LoggerBody",
        "ExternalLoggerBody",
    ]:
        token = f"descriptor.{body_name}.IsDefined"
        if token not in generator:
            fail(f"generator single-body descriptor validation missing {token}")

    for kind, (method_shape, ownership, error_policy, duration_policy, body) in sorted(policies_by_kind.items()):
        if duration_policy == "RuntimeMetric" and ownership != "TraceAndMetric":
            fail(f"RuntimeMetric duration policy requires TraceAndMetric ownership: {kind}")
        if ownership == "TraceAndMetric" and duration_policy != "RuntimeMetric":
            fail(f"TraceAndMetric ownership requires RuntimeMetric duration policy: {kind}")

        expected: tuple[str, str, str, str] | None = None
        if body == "HttpWebRequestBody":
            expected = ("AsyncOrSyncValue", "TraceAndMetric", "HttpStatusAndException", "RuntimeMetric")
        elif body == "DbCommandBody":
            expected = ("AsyncOrSyncValue", "TraceAndMetric", "Exception", "RuntimeMetric")
        elif body == "GrpcClientBody":
            expected = (
                "GrpcUnary" if "GrpcNetClientAsyncUnaryCall" == kind else "GrpcStreaming",
                "Trace",
                "GrpcStatusAndException",
                "None",
            )
        elif body == "MeterProviderBuilderBody":
            expected = ("BuilderRegistration", "Metric", "None", "None")
        elif body == "LoggerBody":
            expected = ("Void", "Log", "RuntimeDelegate", "None")
        elif body == "ExternalLoggerBody":
            expected = ("Void", "Log", "Exception", "None")

        if expected is not None and (method_shape, ownership, error_policy, duration_policy) != expected:
            fail(
                f"InterceptorKind.{kind} {body} policy mismatch: "
                f"actual={(method_shape, ownership, error_policy, duration_policy)} expected={expected}"
            )

        if body == "TraceBody":
            if method_shape not in {"AsyncOrSyncValue", "AsyncOrSyncVoid", "AsyncTask", "AsyncValue"}:
                fail(f"TraceBody descriptor has unsupported method shape: {kind} {method_shape}")
            if ownership not in {"Trace", "TraceAndMetric"}:
                fail(f"TraceBody descriptor must own traces: {kind}")
            if error_policy != "Exception":
                fail(f"TraceBody descriptor must use Exception policy: {kind}")
            if duration_policy == "RuntimeMetric" and f"InterceptorKind.{kind}" in generator:
                descriptor_line = next(
                    line
                    for line in generator.splitlines()
                    if f"InterceptorKind.{kind}" in line and "new InterceptorEmissionDescriptor(" in line
                )
                if "DurationMetric:" not in descriptor_line:
                    fail(f"TraceBody RuntimeMetric descriptor missing DurationMetric descriptor: {kind}")

        if body == "ForwardingBody":
            if method_shape not in {"AsyncValue", "AsyncTask", "BuilderInitialization", "EndpointRegistration"}:
                fail(f"ForwardingBody descriptor has unsupported method shape: {kind} {method_shape}")
            if ownership == "TraceAndMetric":
                if (error_policy, duration_policy) != ("HttpStatusAndException", "RuntimeMetric"):
                    fail(f"TraceAndMetric ForwardingBody must use HTTP status and runtime metric policy: {kind}")
            elif ownership == "Trace":
                if error_policy not in {"Exception", "RuntimeDelegate"} or duration_policy != "None":
                    fail(f"Trace ForwardingBody must delegate exception/runtime policy without duration: {kind}")
            else:
                fail(f"ForwardingBody descriptor has unsupported ownership: {kind} {ownership}")


def collect_generator_target_contract_keys(generator: str) -> set[str]:
    target_contract_keys = set(re.findall(
        r"InterceptorKind\.[A-Za-z0-9]+,\s*\n\s*\"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)\"",
        generator,
    ))
    target_contract_keys.update(parse_contract_keys_call_keys(generator))

    if "GetDbTraceContractKey(instrumentationId)" not in generator:
        fail("DbCommand target must route trace contract keys through GetDbTraceContractKey")
    target_contract_keys.update(parse_switch_helper_keys(generator, "GetDbTraceContractKey"))

    if "GetDbMetricContractKeys(instrumentationId)" not in generator:
        fail("DbCommand target must route metric contract keys through GetDbMetricContractKeys")
    target_contract_keys.update(parse_switch_helper_keys(generator, "GetDbMetricContractKeys"))
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
    for token in [
        "descriptor.TraceBody.IsDefined",
        "descriptor.ForwardingBody.IsDefined",
        "descriptor.HttpWebRequestBody.IsDefined",
        "descriptor.DbCommandBody.IsDefined",
        "descriptor.GrpcClientBody.IsDefined",
        "descriptor.MeterProviderBuilderBody.IsDefined",
        "descriptor.LoggerBody.IsDefined",
        "descriptor.ExternalLoggerBody.IsDefined",
    ]:
        if token not in dispatch_block:
            fail(f"emitter dispatch missing typed body descriptor: {token}")

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
    for token in [
        "ValidateDescriptorCatalog();",
        "Matcher descriptor catalog declares a duplicate interceptor kind",
        "Emission descriptor catalog declares a duplicate interceptor kind",
        "Matcher and emission descriptor catalogs must declare the same interceptor kind set",
        "EnsureTargetDeclaredByMatcher(descriptor, in target);",
        "EnsureKindDeclaredByMatcher(descriptor, target.Kind);",
        "public ulong TargetKindMask { get; }",
        "GetInterceptorKindMask(kind)",
        "Matcher descriptor '",
        "produced undeclared interceptor kind",
    ]:
        if token not in generator:
            fail(f"matcher dispatch must validate descriptor-declared InterceptorKind values: {token}")

    descriptor_kinds = parse_emission_descriptor_kinds(generator)
    descriptor_missing = kinds - descriptor_kinds
    descriptor_extra = descriptor_kinds - kinds
    if descriptor_missing or descriptor_extra:
        fail(
            "InterceptorKind descriptor mismatch: "
            f"missing={sorted(descriptor_missing)} extra={sorted(descriptor_extra)}"
        )

    matcher_kinds = parse_matcher_descriptor_kinds(generator)
    matcher_missing = descriptor_kinds - matcher_kinds
    matcher_extra = matcher_kinds - descriptor_kinds
    if matcher_missing or matcher_extra:
        fail(
            "InterceptorKind matcher descriptor mismatch: "
            f"missing={sorted(matcher_missing)} extra={sorted(matcher_extra)}"
        )

    target_missing = {
        kind
        for kind in kinds
        if f"InterceptorKind.{kind}," not in generator and f"= InterceptorKind.{kind};" not in generator
    }
    if target_missing:
        fail(f"InterceptorKind values missing from target construction: {sorted(target_missing)}")

    verify_interceptor_signal_ownership(generator, kinds)
    verify_interceptor_policy_shapes(generator, kinds)

    target_contract_keys = collect_generator_target_contract_keys(generator)
    matcher_contract_keys = parse_matcher_descriptor_contract_keys(generator)
    missing_matcher_contract_keys = target_contract_keys - matcher_contract_keys
    if missing_matcher_contract_keys:
        fail(f"generator target contract keys missing matcher descriptors: {sorted(missing_matcher_contract_keys)}")

    missing_contract_keys = implemented_signal_keys - target_contract_keys
    extra_contract_keys = target_contract_keys - implemented_signal_keys
    if missing_contract_keys or extra_contract_keys:
        fail(
            "generator target contract key mismatch: "
            f"missing={sorted(missing_contract_keys)} extra={sorted(extra_contract_keys)}"
        )


def verify_generator_keys(artifacts: ModuleType, contract: dict[str, Any]) -> None:
    generator = read_generator_sources()
    contract_source = CONTRACT_PATH.read_text()
    generator_keys = set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', generator))
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

    for token in [
        "SourceGeneratedSignal",
        "TryGetSourceGeneratedSignal",
        "SourceGeneratedSignalPromiseCount",
    ]:
        if token in contract_source or token in generator:
            fail(f"contract/generator must not conflate implemented with source-generated: {token}")

    for token in [
        "ImplementedSignalKeys",
        "SourceInterceptorSignalKeys",
        "RuntimePublicTelemetrySignalKeys",
        "UnsupportedNativeAotSignalKeys",
        "TryGetSourceInterceptorSignal",
        "TryGetImplementedSignal",
        "InterceptorEmissionDescriptor",
        "InterceptorMethodShape",
        "InterceptorSignalOwnership",
        "InterceptorErrorPolicy",
        "InterceptorDurationPolicy",
        "TraceRuntimeHelperDescriptor",
        "TraceStartActivityArgumentKind",
        "TraceDurationMetricDescriptor",
        "TraceDurationMetricArgumentKind",
        "TraceActivityEnrichmentDescriptor",
        "TraceActivityEnrichmentArgumentKind",
        "TraceAsyncObservationDescriptor",
        "TraceAsyncObservationCondition",
        "TraceMethodPrefixKind",
        "new TraceRuntimeHelperDescriptor",
        "new TraceDurationMetricDescriptor",
        "new TraceActivityEnrichmentDescriptor",
        "new TraceAsyncObservationDescriptor",
        "TraceStartActivityArgumentKind.InstrumentationIdAndTargetMethodName",
        "TraceStartActivityArgumentKind.ReceiverTypeAndTargetMethodName",
        "TraceStartActivityArgumentKind.RedisOperationName",
        "TraceStartActivityArgumentKind.TargetMethodName",
        "TraceStartActivityArgumentKind.RabbitMqExchange",
        "TraceDurationMetricArgumentKind.TargetMethodName",
        "TraceActivityEnrichmentArgumentKind.GraphQlExecutionOptions",
        "TraceAsyncObservationCondition.AsyncWithByRefParameters",
        "TraceMethodPrefixKind.InstrumentationIdAndTargetMethodName",
        "descriptor.MethodPrefixKind",
        "descriptor.DurationMetric.AppendMetricStartStatement(builder)",
        "descriptor.DurationMetric.AppendRecordDurationStatement(builder, in target)",
        "descriptor.ActivityEnrichment.Append(builder, in target)",
        "descriptor.AsyncObservation.AppliesTo(in target)",
        "descriptor.AsyncObservation.ObserveAsyncMethod",
    ]:
        if token not in (contract_source if token.startswith(("Implemented", "Source", "Runtime", "Unsupported", "TryGet")) else generator):
            fail(f"contract/generator missing separated descriptor API token: {token}")

    for method_name in re.findall(r"\n    private static void (Append[A-Za-z0-9]+StartActivity)\(", generator):
        fail(f"generator must model trace start activity calls with TraceRuntimeHelperDescriptor, not private emitter method: {method_name}")

    for method_name in [
        "AppendNServiceBusMetricStartStatement",
        "AppendNServiceBusRecordDurationStatement",
        "AppendGraphQlExecutionOptionsStatement",
    ]:
        if method_name in generator:
            fail(f"generator must model reusable trace behavior with descriptors, not private emitter method: {method_name}")

    for token in [
        "TraceActivityExpressionEmitter",
        "TraceStatementEmitter",
        "AppendStartActivityExpression",
        "AppendBeforeActivityStatement",
        "AppendAfterActivityStatement",
        "AppendAfterSuccessStatement",
        "AppendAfterExceptionStatement",
        "string RecordSuccessStatement",
        "string RecordExceptionStatement",
        "TraceBoolProvider",
        "RuntimeObservesAsync",
        "RuntimeObservesAsyncWhen",
        "ShouldRuntimeObserveElasticAsync",
        "TraceStringProvider",
        "MethodPrefixProvider",
        "GetElasticMethodPrefix",
        "ObserveAsyncMethod:",
    ]:
        if token in generator:
            fail(f"generator must model trace async observation with TraceAsyncObservationDescriptor, not token: {token}")

    if "NavigationManager" in generator or "NavigateTo" in generator:
        fail("generator must not synthesize aspnetcore.components.navigate from NavigationManager.NavigateTo")

    for token in FORBIDDEN_GENERATOR_RUNTIME_DISPATCH_TOKENS:
        if token in generator:
            fail(f"generator must not emit runtime dispatch instrumentation: {token}")

    for token in FORBIDDEN_GENERATOR_MECHANISM_TOKENS:
        if token in generator:
            fail(f"generator must not use forbidden instrumentation mechanism: {token}")

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
    verify_runtime_public_telemetry_status_policy()
    verify_runtime_public_telemetry_payload_access_policy()
    verify_sensitive_attribute_emission_policy()
    verify_bounded_activity_name_policy()
    verify_behavior_semantics_contract()
    verify_productive_mechanism_contract()
    print("contract-invariants-ok")


if __name__ == "__main__":
    main()
