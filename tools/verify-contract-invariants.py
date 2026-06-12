#!/usr/bin/env python3
from __future__ import annotations

import importlib.util
import re
from pathlib import Path
from types import ModuleType
from typing import Any


ROOT = Path(__file__).resolve().parents[1]
ARTIFACTS_PATH = ROOT / "tools" / "generate-contract-artifacts.py"
CONTRACT_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "InstrumentationContract.cs"
GENERATOR_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "QylAutoInstrumentationGenerator.cs"
OPTIONS_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "QylAutoInstrumentationOptions.cs"
IDS_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "QylAutoInstrumentationIds.cs"
SEMCONV_ATTRIBUTES_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "QylSemanticAttributes.cs"
SEMCONV_GENERATOR_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "SemConvRegistryGenerator.cs"
RUNTIME_SEMANTICS_PATH = ROOT / "docs" / "RUNTIME_SEMANTICS.md"
HANDOFF_GATE_PATH = ROOT / "tools" / "verify-aot-autoinstrumentation-goal.py"
RUNTIME_PROJECT_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "Qyl.AutoInstrumentation.csproj"
METRIC_METERS_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "QylMetricMeters.cs"
METRIC_NAMES_PATH = ROOT / "src" / "Qyl.AutoInstrumentation" / "QylMetricNames.cs"
RUNTIME_EMISSION_ROOTS = [
    ROOT / "src" / "Qyl.AutoInstrumentation",
    ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners",
    ROOT / "src" / "Qyl.AutoInstrumentation.EntityFrameworkCore",
    ROOT / "src" / "Qyl.AutoInstrumentation.SqlClient",
]
PRODUCTIVE_MECHANISM_ROOTS = [
    ROOT / "src" / "Qyl.AutoInstrumentation",
    ROOT / "src" / "Qyl.AutoInstrumentation.DiagnosticListeners",
    ROOT / "src" / "Qyl.AutoInstrumentation.Hosting",
    ROOT / "src" / "Qyl.AutoInstrumentation.SqlClient",
    ROOT / "src" / "Qyl.AutoInstrumentation.EntityFrameworkCore",
    ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators",
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
REQUIRED_METER_PROVIDER_DELEGATION_TOKEN = "global::Qyl.AutoInstrumentation.QylMetricMeters.GetEnabledMeterNames()"
REQUIRED_INTERCEPTOR_EMITTER_DELEGATION_TOKEN = "global::Qyl.AutoInstrumentation.QylIntercepted"
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
    generator = GENERATOR_PATH.read_text()
    meters = METRIC_METERS_PATH.read_text()
    names = METRIC_NAMES_PATH.read_text()
    metric_implementation_text = "\n".join([generator, meters, names])

    for token in [
        'public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";',
        'public const string AspNetCoreComponentsLifecycleMeterName = "Microsoft.AspNetCore.Components.Lifecycle";',
        'public const string AspNetCoreComponentsServerCircuitsMeterName = "Microsoft.AspNetCore.Components.Server.Circuits";',
        'public const string AspNetCoreComponentsNavigateMetricName = "aspnetcore.components.navigate";',
    ]:
        if token not in contract:
            fail(f"InstrumentationContract must pin the .NET 10 ASP.NET Core components metric proof: {token}")

    if 'public const string AspNetCoreComponentsMeterName = "Microsoft.AspNetCore.Components";' not in meters:
        fail("QylMetricMeters must register the .NET 10 ASP.NET Core components meter")
    for token in [
        "names.Add(AspNetCoreComponentsMeterName);",
        "names.Add(AspNetCoreComponentsLifecycleMeterName);",
        "names.Add(AspNetCoreComponentsServerCircuitsMeterName);",
    ]:
        if token not in meters:
            fail(f"QylMetricMeters must add the .NET 10 ASP.NET Core components meter when ASPNETCORE metrics are enabled: {token}")
    if 'public const string AspNetCoreComponentsNavigate = "aspnetcore.components.navigate";' not in names:
        fail("QylMetricNames must pin the .NET 10 ASP.NET Core components navigate metric")

    for token in [
        "Microsoft.AspNetCore.Hosting",
        "http.server.request.duration",
        "NavigationManager",
        "NavigateTo",
    ]:
        if token in metric_implementation_text:
            fail(f"productive code must not use an obsolete or call-site-invented ASP.NET Core metric proof: {token}")


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
        "descriptor.RecordSuccessStatement",
        "descriptor.RecordExceptionStatement",
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
    generator = GENERATOR_PATH.read_text()
    if "global::Qyl.AutoInstrumentation.QylIntercepted" not in generator:
        fail("generator must delegate intercepted call-sites to the Qyl runtime instrumentation assembly")
    verify_interceptor_emitter_runtime_delegation(generator)

    for token in FORBIDDEN_GENERATOR_INLINE_TELEMETRY_TOKENS:
        if token in generator:
            fail(f"generator must not inline telemetry behavior instead of delegating to runtime: {token}")

    behavior_sources = [GENERATOR_PATH, *sorted((ROOT / "src" / "Qyl.AutoInstrumentation").glob("QylIntercepted*.cs"))]
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


def parse_matcher_descriptor_contract_keys(generator: str) -> set[str]:
    try:
        descriptor_block = generator.split("s_matcherDescriptors =", 1)[1].split("s_emissionDescriptors =", 1)[0]
    except IndexError:
        fail("s_matcherDescriptors block missing from generator")

    matcher_keys = set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', descriptor_block))
    if not matcher_keys:
        fail("s_matcherDescriptors must describe source-visible matcher contract keys")

    return matcher_keys


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
    generator = GENERATOR_PATH.read_text()
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
    ]:
        if token not in (contract_source if token.startswith(("Implemented", "Source", "Runtime", "Unsupported", "TryGet")) else generator):
            fail(f"contract/generator missing separated descriptor API token: {token}")

    if "NavigationManager" in generator or "NavigateTo" in generator:
        fail("generator must not synthesize aspnetcore.components.navigate from NavigationManager.NavigateTo")

    if "http.server.request.duration" in generator or "Microsoft.AspNetCore.Hosting" in generator:
        fail("generator must not use the old ASP.NET Core Hosting meter metric proof")

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
    verify_managed_evidence_boundaries(artifacts, contract)
    verify_handoff_real_demo_coverage(artifacts, contract)
    verify_nativeaot_evidence_is_executable(artifacts, contract)
    verify_generator_keys(artifacts, contract)
    verify_environment_contract(artifacts, contract)
    verify_semconv_attribute_contract()
    verify_metric_contract()
    verify_behavior_semantics_contract()
    verify_productive_mechanism_contract()
    print("contract-invariants-ok")


if __name__ == "__main__":
    main()
