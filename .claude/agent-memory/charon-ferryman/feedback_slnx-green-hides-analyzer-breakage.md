---
name: slnx-green-hides-analyzer-breakage
description: A green `dotnet build slnx` can hide a broken/unconsumable source-generator package; only smoketest/verify catch CS9057 analyzer-load failures
metadata:
  type: feedback
---

A clean `dotnet build Qyl.OpenTelemetry.AutoInstrumentation.slnx` (0 warnings) is NOT
sufficient validation for any change to the `SourceGenerators` project or its Roslyn
package pins.

**Why:** `Directory.Build.props` adds `Microsoft.Net.Compilers.Toolset` repo-wide, so the
whole solution build uses whatever compiler that pin selects. If that pin is a nightly
Roslyn (e.g. `5.9.0-*`), the solution builds green locally — but that override does NOT
travel to package consumers. A consumer building with the in-box SDK compiler (released
SDKs ship Roslyn 5.x ≤ 5.6) hits `CS9057` ("analyzer references a newer compiler") and
gets ZERO interceptors generated. The package becomes structurally unconsumable while the
local build looks perfect.

**How to apply:** Whenever a change touches the `SourceGenerators` csproj, the
`Microsoft.CodeAnalysis.*` / `Microsoft.Net.Compilers.Toolset` pins in
`Directory.Packages.props`, or adds a Roslyn API call, run `bash tools/smoketest.sh`
(and `verify` / `otlp-collector-fixtures`) BEFORE declaring it verified — those build a
fresh consumer in /tmp with the in-box compiler and are the only canaries that catch this.
The pre-push set of build + verify-public-api-baseline + verify-generator-snapshots does
NOT exercise the consumer analyzer-load path. See [[precompilation-experiment-leaked-into-production-generators]].
