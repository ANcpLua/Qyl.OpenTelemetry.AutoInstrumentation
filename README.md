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
| `Qyl.Sdk` | One-line onboarding: OpenTelemetry SDK wiring, OTLP export, collector discovery, session propagation |
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

## Exporting to a collector

The shortest path is `Qyl.Sdk`, which owns all of the wiring below as one call:

```csharp
builder.AddQyl();
```

That activates the qyl listeners, registers the qyl/ASP.NET Core/HttpClient/GenAI/MCP
sources and the full qyl meter inventory (ASP.NET Core, HttpClient, DNS, database,
messaging, runtime, plus the GenAI meters such as `gen_ai.client.token.usage`),
copies `session.id` from the nearest tagged in-process ancestor to descendant spans
(remote parents and unrelated trace branches are not propagated), and exports traces, metrics, and logs over
OTLP — to `OTEL_EXPORTER_OTLP_ENDPOINT` when set, otherwise to a qyl collector
discovered on localhost (4318/4317). GenAI telemetry still requires the one-line agent
opt-in described below.

The rest of this section is the manual wiring for apps that want to own it.

These packages emit `Activity` and `Meter` telemetry; they ship no exporter. The
consuming application wires the OpenTelemetry SDK and chooses where the telemetry
goes. A working setup against the qyl collector adds
`OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol`
alongside `Qyl.OpenTelemetry.AutoInstrumentation.Hosting`, then registers the sources:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-service"))
    .WithTracing(t => t
        .AddSource("Qyl.OpenTelemetry.AutoInstrumentation") // qyl listeners
        .AddSource("Microsoft.AspNetCore")                   // BCL incoming HTTP
        .AddSource("System.Net.Http")                        // BCL outgoing HttpClient
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
```

Configure the exporter through the standard environment variables:
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318`,
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and `OTEL_SERVICE_NAME`.

GenAI applications using `Microsoft.Agents.AI` additionally opt in with
`agent.AsBuilder().UseOpenTelemetry().Build()` and register
`AddSource("Experimental.Microsoft.Agents.AI")` and
`AddSource("Experimental.Microsoft.Extensions.AI")`. The qyl collector ingests the
resulting `gen_ai.*` spans, including sessions and token usage; billed costs come
from provider APIs and model-catalog estimates on the collector side, not from
span attributes. The Anthropic .NET SDK itself emits no telemetry.

## Coverage and evidence

The generated [`coverage matrix`](docs/coverage-matrix.md) is the detailed contract
view. Its current 60 rows comprise:

- 33 implemented signal rows: 26 with NativeAOT runtime evidence and 7 with managed
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
