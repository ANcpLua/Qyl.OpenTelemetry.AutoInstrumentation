#!/usr/bin/env python3
from __future__ import annotations

import re
from pathlib import Path


ROOT = Path(__file__).resolve().parents[1]
YAML_PATH = ROOT / "docs" / "otel-dotnet-auto-60-contract-items.yaml"
CONTRACT_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "InstrumentationContract.cs"
GENERATOR_PATH = ROOT / "src" / "Qyl.AutoInstrumentation.SourceGenerators" / "QylAutoInstrumentationGenerator.cs"
INTENTIONALLY_UNSUPPORTED_DYNAMIC_SIGNAL_KEYS = {"signals.traces.WCFCORE"}
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


def fail(message: str) -> None:
    raise SystemExit(message)


def parse_scalar(block: str, name: str) -> str | None:
    match = re.search(rf"(?m)^  {re.escape(name)}: (.+)$", block)
    if match is None:
        return None

    value = match.group(1).strip()
    if len(value) >= 2 and value[0] == value[-1] == "'":
        return value[1:-1]

    return value


def parse_list(block: str, name: str) -> list[str]:
    match = re.search(rf"(?ms)^  {re.escape(name)}:\n((?:  - .+\n)+)", block)
    if match is None:
        return []

    return [line[4:].strip() for line in match.group(1).splitlines()]


def parse_yaml_items() -> list[dict[str, object]]:
    text = YAML_PATH.read_text()
    try:
        contract_text = text.split("\ncontract_items:\n", 1)[1]
    except IndexError:
        fail("contract_items block missing from YAML")

    items: list[dict[str, object]] = []
    for raw in contract_text.split("\n- kind: "):
        if not raw.strip():
            continue

        block = "- kind: " + raw if raw.startswith("signal") or raw.startswith("global") or raw.startswith("instrumentation") else raw
        first_line, _, rest = block.partition("\n")
        kind = first_line.removeprefix("- kind: ").strip()
        item_block = "\n" + rest
        index_value = parse_scalar(item_block, "index")
        if index_value is None:
            fail(f"YAML item missing index: {kind}")

        items.append(
            {
                "kind": kind,
                "index": int(index_value),
                "key": parse_scalar(item_block, "key"),
                "signal": parse_scalar(item_block, "signal"),
                "instrumentation_id": parse_scalar(item_block, "instrumentation_id"),
                "environment_toggle": parse_scalar(item_block, "environment_toggle"),
                "environment_variable": parse_scalar(item_block, "environment_variable"),
                "not_supported_on": parse_list(item_block, "not_supported_on"),
            }
        )

    return items


def parse_contract_source() -> tuple[list[tuple[int, str, str, str, str]], list[tuple[int, str, str]], list[tuple[int, str, str, str, str]]]:
    text = CONTRACT_PATH.read_text()
    signals = [
        (int(m.group(1)), m.group(2), m.group(3).lower(), m.group(4), m.group(5))
        for m in re.finditer(
            r'Signal\((\d+), "([^"]+)", InstrumentationSignal\.([A-Za-z]+), "([^"]+)", "([^"]+)"',
            text,
        )
    ]
    controls = [
        (int(m.group(1)), m.group(2), m.group(3))
        for m in re.finditer(r'Control\((\d+), "([^"]+)", "([^"]+)"', text)
    ]
    options = [
        (int(m.group(1)), m.group(2), m.group(3), m.group(4).lower(), m.group(5))
        for m in re.finditer(
            r'Option\((\d+), "([^"]+)", "([^"]+)", InstrumentationSignal\.([A-Za-z]+), "([^"]+)"',
            text,
        )
    ]
    return signals, controls, options


def parse_unsupported_keys() -> set[str]:
    text = CONTRACT_PATH.read_text()
    try:
        block = text.split("UnsupportedNativeAotSignalKeys", 1)[1].split("];", 1)[0]
    except IndexError:
        fail("UnsupportedNativeAotSignalKeys block missing")

    return set(re.findall(r'"(signals\.[^"]+)"', block))


def verify_yaml_vs_contract() -> tuple[set[str], set[str]]:
    yaml_items = parse_yaml_items()
    signals, controls, options = parse_contract_source()
    if (len(signals), len(controls), len(options), len(signals) + len(controls) + len(options)) != (37, 7, 16, 60):
        fail(f"wrong InstrumentationContract counts: {len(signals)}/{len(controls)}/{len(options)}")

    by_index = {int(item["index"]): item for item in yaml_items}
    if len(by_index) != 60:
        fail(f"wrong YAML contract item count: {len(by_index)}")

    for index, key, signal, instrumentation_id, environment_toggle in signals:
        item = by_index.get(index)
        if item is None:
            fail(f"YAML missing signal item {index}")
        if item["key"] != key or item["signal"] != signal or item["instrumentation_id"] != instrumentation_id or item["environment_toggle"] != environment_toggle:
            fail(f"signal item mismatch at {index}: YAML={item} CS={(key, signal, instrumentation_id, environment_toggle)}")

    for index, key, environment_variable in controls:
        item = by_index.get(index)
        if item is None:
            fail(f"YAML missing control item {index}")
        if item["key"] != key or item["environment_variable"] != environment_variable:
            fail(f"control item mismatch at {index}: YAML={item} CS={(key, environment_variable)}")

    for index, key, environment_variable, _signal, _instrumentation_id in options:
        item = by_index.get(index)
        if item is None:
            fail(f"YAML missing option item {index}")
        if item["key"] != key or item["environment_variable"] != environment_variable:
            fail(f"option item mismatch at {index}: YAML={item} CS={(key, environment_variable)}")

    yaml_signal_keys = {
        str(item["key"])
        for item in yaml_items
        if item["kind"] == "signal_specific_instrumentation_promise"
    }
    yaml_dotnet_unsupported = {
        str(item["key"])
        for item in yaml_items
        if item["kind"] == "signal_specific_instrumentation_promise"
        and ".NET" in item["not_supported_on"]
    }
    expected_unsupported = yaml_dotnet_unsupported | INTENTIONALLY_UNSUPPORTED_DYNAMIC_SIGNAL_KEYS
    contract_unsupported = parse_unsupported_keys()
    if expected_unsupported != contract_unsupported:
        fail(f"unsupported set mismatch: expected={sorted(expected_unsupported)} CS={sorted(contract_unsupported)}")

    return yaml_signal_keys, contract_unsupported


def verify_generator_keys(yaml_signal_keys: set[str], unsupported_keys: set[str]) -> None:
    generator = GENERATOR_PATH.read_text()
    generator_keys = set(re.findall(r'"(signals\.(?:traces|metrics|logs)\.[A-Z0-9]+)"', generator))
    source_generated_keys = yaml_signal_keys - unsupported_keys
    if generator_keys != source_generated_keys:
        fail(
            "generator signal key mismatch: "
            f"missing={sorted(source_generated_keys - generator_keys)} "
            f"extra={sorted(generator_keys - source_generated_keys)}"
        )

    if generator_keys & unsupported_keys:
        fail(f"unsupported keys leaked into generator: {sorted(generator_keys & unsupported_keys)}")

    if "NavigationManager" in generator or "NavigateTo" in generator:
        fail("generator must not synthesize aspnetcore.components.navigation from NavigationManager.NavigateTo")

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


def main() -> None:
    yaml_signal_keys, unsupported_keys = verify_yaml_vs_contract()
    verify_generator_keys(yaml_signal_keys, unsupported_keys)
    print("contract-invariants-ok")


if __name__ == "__main__":
    main()
