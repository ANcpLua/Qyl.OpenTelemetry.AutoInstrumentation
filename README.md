# Qyl.Telemetry.AutoInstrumentation

Managed automatic instrumentation for .NET 10 applications, including NativeAOT
consumers. The package uses compiler-generated Roslyn interceptors, build assets,
BCL telemetry primitives, public diagnostic hooks, and module-initializer bootstrap.
It does not use a CLR profiler, startup hooks, ReJIT, runtime IL rewriting, or dynamic
plugin loading.

Roslyn interceptors are supported by this repository's .NET SDK 10.0.400. See the official
[`interceptors.md`](https://github.com/dotnet/roslyn/blob/main/docs/features/interceptors.md)
contract.

## Packages

| Package | Responsibility |
| --- | --- |
| `Qyl.Telemetry.Hosting` | One-line onboarding: OpenTelemetry SDK wiring, OTLP export, collector discovery, session propagation |
| `Qyl.Telemetry.AutoInstrumentation` | Core runtime, compiler-facing ABI, build assets, and source generator |
| `.Hosting` | Generic DI and process bootstrap |
| `.DiagnosticListeners` | Framework/library diagnostic event consumption |
| `.EntityFrameworkCore` | EF Core integration |
| `.SqlClient` | Microsoft.Data.SqlClient integration |

Add the package that owns the integration you need. The supported zero-configuration
consumer path is a `PackageReference`; build and analyzer assets flow through NuGet.

The family ships as one line; `Directory.Build.props` owns its version and
`Directory.Packages.props` the semantic-conventions pin. Its major is the compile-time ABI: a `12.x` package pairs with `QylGeneratedCodeAbi.V12`
and nothing else, which is why the number is ahead of the rest of qyl and does not move
with the product version.

**Coming from 8.x?** These are new package IDs, not new versions of the old ones.
`Qyl.OpenTelemetry.AutoInstrumentation*` and `Qyl.Sdk` stop at `8.5.0` and are not
updated further; change the ID and take the current version. `Qyl.Telemetry.Hosting` is the
successor to `Qyl.Sdk`, and `builder.AddQyl()` is
unchanged. The generated-code ABI anchor is `QylGeneratedCodeAbi.V12` in the
`Qyl.Telemetry.AutoInstrumentation.GeneratedCode` namespace — the anchor tracks the
package major, so it moved from `V11` with the 12.0.0 line — so a stale generated
interceptor cannot bind to the new runtime — it fails to compile rather than
misbehaving. The emitted scope names move to the package family in 10.0.0: the
`ActivitySource` is `Qyl.Telemetry.AutoInstrumentation` and the two qyl meters
are `Qyl.Telemetry.AutoInstrumentation.Database` and
`Qyl.Telemetry.AutoInstrumentation.NServiceBus`. Update `AddSource(...)`,
`AddMeter(...)` and `OTEL_DOTNET_AUTO_TRACES_ADDITIONAL_SOURCES`. This package
emits the `Qyl.Telemetry.AutoInstrumentation*` scope names and nothing else, and
makes no compatibility promise for the old spellings. The strings are owned by
the semantic-convention registry, not by this repository — the code reads them
from `QylTelemetryNames.Scopes`.

```bash
dotnet add package Qyl.Telemetry.Hosting
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

The shortest path is `Qyl.Telemetry.Hosting`, which owns all of the wiring below as one call:

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
`OTEL_EXPORTER_OTLP_ENDPOINT` when set, otherwise to `QYL_ENDPOINT` when that is
set, otherwise to a qyl collector discovered on localhost (4318/4317).
`QYL_ENDPOINT` names a collector for the qyl exporters alone, where the standard
variable would redirect every OTLP exporter in the process. It also registers the
exact library telemetry paths
listed below; wrapper-based libraries still require their explicit one-line opt-in.

The rest of this section is the manual wiring for apps that want to own it.

The lower-level instrumentation packages emit `Activity` and `Meter` telemetry; they
ship no exporter. An application that does not use `Qyl.Telemetry.Hosting` wires the OpenTelemetry
SDK and chooses where the telemetry goes. A working setup against the qyl collector
adds
`OpenTelemetry.Extensions.Hosting` and `OpenTelemetry.Exporter.OpenTelemetryProtocol`
alongside `Qyl.Telemetry.AutoInstrumentation.Hosting`, then registers the sources:

```csharp
builder.Services.AddOpenTelemetry()
    .ConfigureResource(r => r.AddService("my-service"))
    .WithTracing(t => t
        .AddSource("Qyl.Telemetry.AutoInstrumentation") // the qyl scope name — see note below
        .AddOtlpExporter());
builder.Logging.AddOpenTelemetry(o => o.AddOtlpExporter());
```

Configure the exporter through the standard environment variables:
`OTEL_EXPORTER_OTLP_ENDPOINT=http://localhost:4318`,
`OTEL_EXPORTER_OTLP_PROTOCOL=http/protobuf`, and `OTEL_SERVICE_NAME`.

Do not also subscribe to `Microsoft.AspNetCore` or `System.Net.Http` traces when the
qyl listeners own those operations; doing so exports the same request twice. Azure
SDK tracing is the first-party exception: `Qyl.Telemetry.Hosting` enables
`Azure.Experimental.EnableActivitySource`, subscribes `Azure.*`, and normalizes the
exported Azure spans. A manually wired application must make those two choices
explicitly if it wants Azure SDK spans.

### MassTransit

Bound at compile time to the MassTransit version the consumer has installed: this
package references no MassTransit assembly, and the source generator matches
`IPublishEndpoint`, `ISendEndpoint` and `ISendEndpointProvider` `Publish`/`Send`
calls in the consumer's own compilation. Verified against MassTransit `8.5.10`
(Apache-2.0). For 9.x: it works if the intercepted signatures are unchanged; that
is not tested here and is not a commitment. MassTransit 9 is commercially
licensed and is neither referenced nor redistributed by this package, and the
repository's own demo pin is the range `[8.5.10,9.0.0)`.

### AI, MCP, and CoreWCF paths in 12.0

These are version-pinned library-hook claims, not provider- or protocol-wide claims.
The exact `ModelContextProtocol` 2.2.0 client/server path has strict NativeAOT
evidence; the other paths in this table have managed evidence only:

| Library path | Application opt-in | Signals registered by `Qyl.Telemetry.Hosting` | Integration ID |
| --- | --- | --- | --- |
| `Microsoft.Extensions.AI` 10.9.0 | `chatClient.AsBuilder().UseOpenTelemetry().Build()` | traces and metrics from `Experimental.Microsoft.Extensions.AI` | `MICROSOFTEXTENSIONSAI` |
| `Microsoft.Agents.AI` 1.20.0 | `agent.AsBuilder().UseOpenTelemetry().Build()` | traces and metrics from `Experimental.Microsoft.Agents.AI` | `MICROSOFTAGENTSAI` |
| `Microsoft.Agents.AI.Workflows` 1.20.0 | `WorkflowBuilder.WithOpenTelemetry()` | traces from `Microsoft.Agents.AI.Workflows` | `MICROSOFTAGENTSAIWORKFLOWS` |
| `ModelContextProtocol` 2.2.0 | none; the official client/server SDK emits automatically | managed and strict NativeAOT traces from `Experimental.ModelContextProtocol` | `MCP` |
| `CoreWCF.Http` 1.9.1 | none; CoreWCF emits server activities | managed traces from `CoreWCF.Primitives` | `WCFCORE` |

MCP metrics are intentionally not registered: the official instruments attach
dynamic tool and resource names as dimensions, which conflicts with qyl's bounded-cardinality
policy. The 12.0 contract does not claim direct OpenAI SDK instrumentation, raw Anthropic SDK
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

## Design

`DESIGN.md` records how a library gets instrumented: whether `AddSource("<name>")` alone delivers
spans decides between subscribing to the library's own `ActivitySource` and generating a Roslyn
interceptor, and the audit behind that decision per library.

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
- Generic HTTP header capture never records the reserved `Mcp-Param-*` namespace.
  Those headers mirror MCP tool arguments; any argument-content capture belongs to an
  MCP-specific, explicitly enabled policy rather than the HTTP instrumentation layer.
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
