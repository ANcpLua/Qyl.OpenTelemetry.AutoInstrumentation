# Qyl.OpenTelemetry.AutoInstrumentation

Managed automatic instrumentation for .NET 10 applications, including NativeAOT
consumers. The package uses compiler-generated Roslyn interceptors, build assets,
BCL telemetry primitives, public diagnostic hooks, and module-initializer bootstrap.
It does not use a CLR profiler, startup hooks, ReJIT, runtime IL rewriting, or dynamic
plugin loading.

Roslyn interceptors are supported by this repository's .NET SDK 10.0.302. See the official
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
dotnet add package Qyl.Sdk
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
using Qyl;

builder.AddQyl();
```

That activates the qyl listeners; registers the single qyl source for qyl-owned
ASP.NET Core, HttpClient, gRPC, and database spans plus the enabled first-party
library sources; and registers the native and qyl-owned meter inventory (ASP.NET
Core, HttpClient, DNS, database, messaging, and runtime). It copies `session.id`
from the nearest tagged in-process
ancestor to descendant spans (remote parents and unrelated trace branches are not
propagated); and exports traces, metrics, and logs over OTLP — to
`OTEL_EXPORTER_OTLP_ENDPOINT` when set, otherwise to a qyl collector discovered on
localhost (4318/4317). It also registers the exact library telemetry paths
listed below; wrapper-based libraries still require their explicit one-line opt-in.

The rest of this section is the manual wiring for apps that want to own it.

The lower-level instrumentation packages emit `Activity` and `Meter` telemetry; they
ship no exporter. An application that does not use `Qyl.Sdk` wires the OpenTelemetry
SDK and chooses where the telemetry goes. A working setup against the qyl collector
adds
`OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol`
alongside `Qyl.OpenTelemetry.AutoInstrumentation.Hosting`, then registers the sources:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-service"))
    .WithTracing(t => t
        .AddSource("Qyl.OpenTelemetry.AutoInstrumentation") // qyl listeners
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
```

Configure the exporter through the standard environment variables:
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318`,
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and `OTEL_SERVICE_NAME`.

Do not also subscribe to `Microsoft.AspNetCore` or `System.Net.Http` traces when the
qyl listeners own those operations; doing so exports the same request twice. Azure
SDK tracing is the first-party exception: `Qyl.Sdk` enables
`Azure.Experimental.EnableActivitySource`, subscribes `Azure.*`, and normalizes the
exported Azure spans. A manually wired application must make those two choices
explicitly if it wants Azure SDK spans.

### AI, MCP, and CoreWCF paths in 8.0

These are version-pinned library-hook claims, not provider- or protocol-wide claims.
The exact `ModelContextProtocol` 1.4.1 client/server path has strict NativeAOT
evidence; the other paths in this table have managed evidence only:

| Library path | Application opt-in | Signals registered by `Qyl.Sdk` | Integration ID |
| --- | --- | --- | --- |
| `Microsoft.Extensions.AI` 10.8.0 | `chatClient.AsBuilder().UseOpenTelemetry().Build()` | traces and metrics from `Experimental.Microsoft.Extensions.AI` | `MICROSOFTEXTENSIONSAI` |
| `Microsoft.Agents.AI` 1.13.0 | `agent.AsBuilder().UseOpenTelemetry().Build()` | traces and metrics from `Experimental.Microsoft.Agents.AI` | `MICROSOFTAGENTSAI` |
| `Microsoft.Agents.AI.Workflows` 1.13.0 | `WorkflowBuilder.WithOpenTelemetry()` | traces from `Microsoft.Agents.AI.Workflows` | `MICROSOFTAGENTSAIWORKFLOWS` |
| `ModelContextProtocol` 1.4.1 | none; the official client/server SDK emits automatically | managed and strict NativeAOT traces from `Experimental.ModelContextProtocol` | `MCP` |
| `CoreWCF.Http` 1.9.1 | none; CoreWCF emits server activities | managed traces from `CoreWCF.Primitives` | `WCFCORE` |

MCP metrics are intentionally not registered: the official instruments attach
dynamic tool and resource names as dimensions, which conflicts with qyl's bounded-cardinality
policy. The 8.0 contract does not claim direct OpenAI SDK instrumentation, raw Anthropic SDK
instrumentation, `Azure.AI.Inference`, Amazon Bedrock, or A2A.

Every path is enabled by default when its signal is enabled. Set the applicable
signal-specific variable to `false` to disable it:

- `MICROSOFTEXTENSIONSAI`:
  `OTEL_DOTNET_AUTO_TRACES_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED` and
  `OTEL_DOTNET_AUTO_METRICS_MICROSOFTEXTENSIONSAI_INSTRUMENTATION_ENABLED`.
- `MICROSOFTAGENTSAI`:
  `OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED` and
  `OTEL_DOTNET_AUTO_METRICS_MICROSOFTAGENTSAI_INSTRUMENTATION_ENABLED`.
- `MICROSOFTAGENTSAIWORKFLOWS`:
  `OTEL_DOTNET_AUTO_TRACES_MICROSOFTAGENTSAIWORKFLOWS_INSTRUMENTATION_ENABLED`.
- `MCP`: `OTEL_DOTNET_AUTO_TRACES_MCP_INSTRUMENTATION_ENABLED`.
- `WCFCORE`: `OTEL_DOTNET_AUTO_TRACES_WCFCORE_INSTRUMENTATION_ENABLED`.

The global `OTEL_DOTNET_AUTO_INSTRUMENTATION_ENABLED` and per-signal
`OTEL_DOTNET_AUTO_{TRACES|METRICS|LOGS}_INSTRUMENTATION_ENABLED` switches still take
precedence.

## Coverage and evidence

The generated [`coverage matrix`](docs/coverage-matrix.md) is the detailed contract
view. It keeps NativeAOT runtime evidence, managed runtime evidence, configuration
bindings, and unsupported rows separate. A configuration binding is not runtime
instrumentation, and the matrix is generated from the declared contracts rather than
being independent empirical proof. Runtime claims are backed by executable demos or
consumers named in the underlying ownership contracts.

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
