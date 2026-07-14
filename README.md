# Qyl.OpenTelemetry.AutoInstrumentation

Managed automatic instrumentation for .NET 10 applications, including NativeAOT
consumers. The package uses compiler-generated Roslyn interceptors, build assets,
BCL telemetry primitives, public diagnostic hooks, and module-initializer bootstrap.
It does not use a CLR profiler, startup hooks, ReJIT, runtime IL rewriting, or dynamic
plugin loading.

Roslyn interceptors are supported by this repository's .NET SDK 10.0.301. See the official
[`interceptors.md`](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md)
contract.

## Packages

| Package | Responsibility |
| --- | --- |
| `Qyl.OpenTelemetry.AutoInstrumentation` | Core runtime, compiler-facing ABI, build assets, and source generator |
| `.Hosting` | Generic DI and process bootstrap |
| `.DiagnosticListeners` | Framework/library diagnostic event consumption |
| `.EntityFrameworkCore` | EF Core integration |
| `.SqlClient` | Microsoft.Data.SqlClient integration |

Add the package that owns the integration you need. The supported zero-configuration
consumer path is a `PackageReference`; build and analyzer assets flow through NuGet.

```bash
dotnet add package Qyl.OpenTelemetry.AutoInstrumentation.Hosting
```

## How it works

1. The source generator discovers supported source-visible calls and emits ordinary
   C# methods annotated with `[InterceptsLocation]`.
2. `build` and `buildTransitive` assets include the compiler-facing generator and
   enable the generated namespace in consumers.
3. Runtime helpers and diagnostic listeners emit bounded `Activity` and `Meter`
   telemetry using the referenced semantic-convention vocabulary.
4. Package-specific bootstrap activates the applicable listeners once per process.

Where a framework exposes a first-class DI or runtime hook, the package uses that
hook. Interception is reserved for source-visible calls that require compile-time
ownership.

## Coverage and evidence

The generated [`coverage matrix`](docs/coverage-matrix.md) is the detailed contract
view. Its current 60 rows comprise:

- 33 implemented signal rows: 30 with NativeAOT runtime evidence and 3 with managed
  runtime evidence;
- 19 configuration rows: 12 option bindings and 7 control bindings;
- 8 unsupported NativeAOT rows, including four CLR-profiler/.NET Framework-only options.

Those categories are intentionally separate. A configuration binding is not runtime
instrumentation, and the matrix is generated from the declared contract rather than
being independent empirical proof. Runtime claims are backed by executable demos or
consumers named in the underlying ownership contract.

The NativeAOT boundary applies to this compile-time/managed substrate. It does not
claim parity with the CLR-profiler OpenTelemetry .NET automatic instrumentor, and it
does not imply that every third-party library itself publishes warning-free under
NativeAOT.

## Limitations

- Only source-visible call sites can be intercepted. Calls hidden in compiled
  dependencies, reflection, or dynamic dispatch need a public runtime hook or remain
  unsupported.
- Some integrations are managed-only because the instrumented library requires
  runtime code generation.
- Query text and other sensitive or high-cardinality values remain opt-in or redacted
  according to the package options and upstream OpenTelemetry controls.
- Generator snapshots prove emitted source shape; protocol interoperability requires
  a real OTLP receiver and structural decoding of official protobuf messages.

## Verify

Run the complete local gate:

```bash
python3 tools/verify-aot-autoinstrumentation-goal.py
```

That gate builds the package and demo solutions, validates generated artifacts and
public API baselines, and executes managed/NativeAOT consumer evidence.

## License

Apache-2.0
