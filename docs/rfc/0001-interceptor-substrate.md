# RFC 0001: AOT-native source interceptor substrate

Status: draft for review

## Summary

This proposal defines qyl's AOT-native auto-instrumentation substrate: a Roslyn
incremental source generator discovers source-visible call sites, obtains Roslyn
`InterceptableLocation` data through `SemanticModel.GetInterceptableLocation`, and emits
C# methods annotated with `[InterceptsLocation]`. The emitted methods call public,
AOT-safe qyl runtime helpers and are compiled into the consumer application by the normal
.NET compiler and NativeAOT toolchain.

The substrate is intentionally complementary to CLR-profiler auto-instrumentation. It is
not a CLR profiler, not runtime IL rewriting, not a startup hook, not an
`AssemblyLoadContext` plugin, and not reflection-based dynamic patching.

## Problem

Existing .NET auto-instrumentation normally relies on runtime attach, profiler callbacks,
startup hooks, dynamic assembly loading, or IL rewriting. Those mechanisms are useful for
JIT applications, but they do not provide a clean NativeAOT story:

- The NativeAOT executable has no JIT-time rewrite point.
- Runtime-loaded instrumentation assemblies fight trimming and static analysis.
- Reflection-heavy discovery either warns under trim/AOT analyzers or requires roots that
  defeat the goal of predictable publication.
- Silent build-asset gaps can produce a consumer that compiles but has no interceptors.

For qyl, AOT compatibility is the product axis. If a feature cannot survive
`dotnet publish -p:PublishAot=true` with trim/AOT warnings treated as release-gate
failures, it is not part of this substrate.

## Proposed substrate

The substrate has three explicit layers:

1. Discovery: an incremental generator inspects source-visible invocations and matches
   supported methods without relying on runtime reflection.
2. Encoding: the generator calls Roslyn's `GetInterceptableLocation` API and writes the
   returned version/data into `[InterceptsLocation]` attributes in generated C#.
3. Runtime: each generated interceptor calls qyl runtime helper APIs that use public
   library surfaces, `DiagnosticListener`, `Activity`, and bounded OpenTelemetry
   attributes.

The generated code must be ordinary C# that NativeAOT can compile. It must not require
profiler registration, ReJIT, runtime IL rewrite, dynamic `Assembly.Load`, or
`Activator.CreateInstance`.

## Build asset contract

The package must carry all compiler-facing substrate assets:

- `analyzers/dotnet/cs/Qyl.AutoInstrumentation.SourceGenerators.dll`
- `build/Qyl.AutoInstrumentation.targets`
- `build/Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs`
- `buildTransitive/Qyl.AutoInstrumentation.targets`
- `buildTransitive/Qyl.AutoInstrumentation.InterceptsLocationAttribute.g.cs`

`build/` and `buildTransitive/` intentionally contain the same core target content. The
target enables the interceptors namespace and adds the local
`InterceptsLocationAttribute` source. A guard property prevents duplicate imports when a
direct package and a transitive package both try to bring in the same core assets.

`PackageReference` is the zero-config consumer path. A consumer should be able to add the
package and have the analyzer plus targets participate in compilation automatically.

`ProjectReference` is a dogfooding path, not a magic NuGet replacement. A bare runtime
`ProjectReference` cannot force the referenced project's analyzer/build assets into the
consumer by MSBuild design. The supported project-reference proof path wires the runtime
project, generator analyzer, and core target explicitly so local development exercises the
same generated-interceptor substrate instead of silently compiling without interceptors.

## Runtime rules

Runtime helper APIs must keep the hot path compatible with NativeAOT and telemetry scale:

- No profiler, startup hook, ReJIT, runtime IL rewrite, `AssemblyLoadContext`, dynamic
  `Assembly.Load`, or reflection-based instrumentation dispatch.
- No unbounded span names. Span and activity names must not include full URLs, request
  paths, query strings, IDs, exception messages, or caller-supplied arbitrary text.
- Stable OpenTelemetry attributes are emitted by default; deprecated aliases can be
  consumed as inputs but must not be re-emitted as canonical output.
- Sensitive raw values such as `url.full`, `url.path`, and `db.query.text` are gated off by
  default.
- Metric instruments and activity sources are process-level owners, not per-request or
  per-interceptor allocations.
- Conformance/self-telemetry processors stay opt-in when they add per-span work.

## Verification contract

This repo gates the substrate through executable checks rather than documentation claims:

- Package layout verification confirms analyzer/build/buildTransitive assets exist and
  forbids profiler/startup-hook/IL-rewrite/runtime-load tokens in those package assets.
- ProjectReference behavior verification proves the bare runtime `ProjectReference`
  limitation and the explicit dogfooding path.
- Source-interceptor consumer verification proves generated interceptors execute under
  managed and NativeAOT consumers.
- The smoke test packs the current tree, creates PackageReference and ProjectReference
  scratch consumers, runs JIT and NativeAOT binaries, and checks deterministic output.
- The AOT warning gate fails on IL2xxx, IL3xxx, IL4xxx, or CA warnings in NativeAOT
  publish logs for the supported smoke consumers.
- Public API baseline verification prevents accidental package surface drift.

These gates are part of the substrate definition. If a future change bypasses them, it is
not a substrate improvement; it is an unverified instrumentation path.

## Contribution shape

The upstream contribution is not "rewrite the CLR profiler in qyl." The contribution is a
separate AOT-native substrate that can coexist with profiler-based instrumentation:

- A shared vocabulary for source-visible interceptor descriptors.
- Golden generated-code fixtures for `[InterceptsLocation]` output.
- AOT smoke consumers for library integrations that are reachable from source-level call
  sites.
- A clear split between compile-time interception and runtime diagnostic listener
  extraction.
- Explicit "not reachable by source interception" gaps where profiler/runtime approaches
  remain the right tool.

This lets upstream and downstream projects reason about AOT auto-instrumentation without
pretending that profiler mechanics survive NativeAOT unchanged.

## Current limitations

- Only source-visible call sites can be intercepted. Calls hidden behind reflection,
  generated binaries, dynamic dispatch without source, or external compiled assemblies are
  outside this substrate.
- Libraries that emit useful framework `ActivitySource`, `Meter`, or `DiagnosticListener`
  data may be better consumed directly than wrapped through an interceptor.
- Some third-party libraries may publish under NativeAOT only with their own warnings or
  app-side constraints. Those warnings belong to that library boundary and must be called
  out instead of hidden inside qyl.
- Benchmarks, collector-backed OTLP transport fixtures, and release evidence are separate
  quality gates still needed for a complete 100/100 score. Generator-output snapshots, the
  NativeAOT web API proof, and canonical OTLP-shaped fixtures are now covered by committed
  gates.

// validated 2026-06-05 18:34 CEST by tools/verify-rfc-artifact.py and tools/verify-aot-autoinstrumentation-goal.py
