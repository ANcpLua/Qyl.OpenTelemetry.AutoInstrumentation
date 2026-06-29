# Telemetry Capability Graph (TCG) — exchange spec

> Status: **v0.1.0-draft.** The generator (pillar 2) and the runtime OTel-`LogRecord` channel ship; the
> static build artifact and the remote queryable endpoint below are the next steps, marked **(planned)**.
> Document current-tree truth — do not describe a planned channel as if it exists.

## Why this exists

Every observability tool today is **pull-by-observation**: a backend learns what a service emits by
receiving samples over time, and never knows whether it has seen the whole surface. A qyl binary can
do the opposite — **declare** its complete possible OpenTelemetry surface, deterministically, before
a single span is sampled, because that surface is a compile-time fact (source-generated interceptors
+ a static contract + a referenced semconv vocabulary). The TCG is that declaration in a
vendor-neutral form any external entity can consume. It is generated, never hand-written, so it
cannot drift from what the binary actually emits.

The schema is [`schema/telemetry-capability-graph.schema.json`](schema/telemetry-capability-graph.schema.json)
(JSON Schema 2020-12). The producer is `TelemetryCapabilityGraphGenerator`, which bakes the document into the core assembly
as the public type `QylTelemetryCapabilityGraph` (`.Json` / `.SchemaVersion` / `.CapabilityCount`).
The generator fills the manifest body via a `partial` method that is elided when it has not run, so
the public surface always compiles (a normal core build bakes the real manifest).

## Document shape

```jsonc
{
  "schemaVersion": "0.1.0-draft",
  "generator": "Qyl.OpenTelemetry.AutoInstrumentation.SourceGenerators.TelemetryCapabilityGraphGenerator",
  "capabilityCount": 60,
  "signals": { "traces": 44, "metrics": 10, "logs": 5 },
  "capabilities": [
    {
      "id": "OTEL_DOTNET_AUTO_CONTRACT_003",   // stable cross-version join key
      "key": "signals.traces.ASPNETCORE",
      "signal": "traces",                       // traces | metrics | logs | none
      "lane": "RuntimePublicTelemetry",
      "status": "Implemented",
      "payloadAccess": "TypedPublic",           // TypedPublic | ReflectionRequired | NotApplicable
      "provenance": "runtime",                  // compile-time | runtime | control | unsupported | unknown
      "instrumentationId": "ASPNETCORE",
      "supportedVersions": "*",
      "owner": "Qyl public telemetry listeners/meters",
      "libraries": ["ASP.NET Core"],
      "attributes": []                          // declared semconv keys; populated as lanes fill the contract
    }
  ]
}
```

## The `provenance` vocabulary (the load-bearing field)

This is the field a sampling consumer can never recover. It answers "where does each capability's
meaning come from?":

| provenance | meaning | derived from lane |
|---|---|---|
| `compile-time` | attribute keys are owned at compile time by an interceptor or framework-init | `SourceInterceptor`, `FrameworkInitialization` |
| `runtime` | attribute *values* are read at runtime from typed payloads by a listener | `RuntimePublicTelemetry`, `OfficialLibraryHook` |
| `control` | a control surface (env var / option), not a span/metric/log itself | `EnvironmentControl`, `InstrumentationOption` |
| `unsupported` | not available on the NativeAOT substrate | `UnsupportedNativeAot` |
| `unknown` | catch-all; consumers MUST map unrecognized values here | — |

Provenance is the bridge between the North Star halves: *semantic ownership trends to compile time,
runtime owns observations.* `runtime` capabilities are exactly where DiagnosticListeners are
load-bearing and cannot be deleted (`docs/experiments/precompilation-verdict.md`: ~95% of attribute
values are runtime-only).

## Versioning & compatibility

- `schemaVersion` is semver. Within a **major**, changes are additive: new object properties, new
  enum members, new capabilities. A `-draft` pre-release may still change shape.
- **Producers** emit all `required` fields and may add properties.
- **Consumers** MUST: ignore unknown object properties; treat unknown `signal` / `lane` / `status` /
  `payloadAccess` / `provenance` members as the documented catch-all (`unknown` for provenance,
  otherwise pass-through); join across versions on `capability.id`, never on array index.
- A breaking change (removing/renaming a field, changing a field's meaning) bumps the **major** and
  is announced in the repo CHANGELOG.

## Publication channels (how an external entity gets the TCG)

1. **In-binary constant + public accessor (shipped).** `QylTelemetryCapabilityGraph.Json` /
   `.SchemaVersion` / `.CapabilityCount`. The authoritative source; every other channel is derived
   from it.
2. **OTel LogRecord at boot (shipped).** The `Qyl.OpenTelemetry.AutoInstrumentation.Publishing` package's
   `AddQylTelemetryCapabilityGraphPublisher()` registers a hosted service that emits the document once at
   host startup through `ILogger` — so when the app has OpenTelemetry logging + an OTLP exporter wired, it
   becomes a true OTLP `LogRecord`, and the exporter stays app-owned (the package takes no OpenTelemetry
   SDK dependency). Mapping:
   - `LogRecord.EventId.Name` = `qyl.telemetry_capability_graph`
   - `LogRecord.Body` = the TCG JSON (string, via the log formatter)
   - severity = `Information`
   - `LogRecord.Attributes`: `qyl.tcg.schema_version`, `qyl.tcg.capability_count`
   - Proven end-to-end by `demos/Qyl.RealTcgPublishingDemo` (`tools/verify-tcg-publishing-demo.py`).
3. **Static build artifact (planned).** `app.telemetry-manifest.json` written at build time (MSBuild
   step extracting the constant) for CI / compliance / offline consumers.
4. **Queryable surface (partial).** The public accessor above already returns the document in-process;
   exposing it over the wire (a diagnostic endpoint or CLI) so a running service answers "what is your
   complete telemetry surface?" remotely is still planned.

## Consumer patterns (what the TCG unlocks)

- **Backend pre-provisioning.** Read the TCG on first contact; create dashboards/alerts for the full
  declared surface before span #1 arrives. Group by `signal`; size cardinality budgets from
  `provenance` (runtime keys carry values, compile-time keys are fixed).
- **Collector validation.** A collector holding the TCG drops/flags telemetry whose attribute keys
  are not in the declared surface — edge-enforced anti-drift and anti-cardinality. (Pairs with the
  existing `SemConvConformanceProcessor`, which does the in-process version under
  `QYL_CONFORMANCE_ENABLED=1`.)
- **CI gate / typed telemetry API.** Diff a PR's TCG against the merge base: a new capability is a new
  observable surface; a removed one is a breaking change for downstream consumers. Telemetry becomes
  a versioned contract, joined on `capability.id`.
- **Cross-service typing.** Service A's TCG declares what it emits; a mesh or service B knows the
  upstream telemetry shape statically.

## Relationship to the generator

The TCG is generated output (see AGENTS.md → *Generated and evidence files*): its single source of
truth is `TelemetryCapabilityGraphGenerator` + `InstrumentationContract`. To change the TCG, change
the contract or the generator and rebuild — never hand-edit the emitted constant. Validate with:

```bash
dotnet build src/Qyl.OpenTelemetry.AutoInstrumentation/Qyl.OpenTelemetry.AutoInstrumentation.csproj \
  -p:EmitCompilerGeneratedFiles=true
# then validate the emitted constant against schema/telemetry-capability-graph.schema.json
```
