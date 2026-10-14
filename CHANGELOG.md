# Changelog

Notable changes to the `Qyl.Telemetry.*` package family. Versions are owned by `<Version>` in
`Directory.Build.props`. The local release gates run before `./release.sh` packs and publishes
that exact version, creates the matching `v*` tag and GitHub release, and waits for all package indexes. Indexed
packages are then exercised in clean managed and NativeAOT consumers.

## [24.1.1] - 2026-10-14

### Security

- `Qyl.Telemetry.AutoInstrumentation.SqlClient` depends on `System.Security.Cryptography.Pkcs`
  10.0.13. `Microsoft.Data.SqlClient` 7.1.1, the newest release, brings 9.0.18, which the
  advisories of 2026-10-13 (GHSA-846f-f764-76mx, GHSA-2fmv-rv2c-59x7) cover; the floor reaches
  consumers through the package.

### Changed

- The build host and the SqlClient, Oracle and WCF demos reference the patched
  `System.Security.Cryptography.Pkcs` and `.Xml` 10.0.13 (Fallout.Common brought 10.0.10 of both;
  GHSA-7hqp-7c5m-4vq6, GHSA-c399-9853-6jmj, GHSA-r955-8q5j-3h83); NuGet audit failed those
  restores.
- The demo lanes' local collector is the `qyl` tool 12.2.0.

## [24.1.0] - 2026-10-14

### Added

- `Qyl.Telemetry.AutoInstrumentation.dll` carries
  `[assembly: AssemblyMetadata("QylSemanticConventionsVersion", "9.9.2")]`, the
  `Qyl.Telemetry.SemanticConventions` release it compiled against, taken at build time from the
  resolved reference rather than typed again. The nuspec dependency on
  `Qyl.Telemetry.SemanticConventions` names the same version as its lower bound, and
  `./eng/build.sh PackageLayout` checks both against the pin.

### Changed

- `Directory.Packages.props` is the one place that names the semantic-convention release. The live
  check clones the registry at `v` + the `Qyl.Telemetry.SemanticConventions` pin;
  `tools/live-check.env` and its hand-kept `SEMCONV_REF` are deleted, and `./eng/build.sh Contracts`
  fails when a tracked file (the CHANGELOG's history aside) hard-codes a `SEMCONV_REF` or a registry
  tag that names another release.
- The `release.sh` dry run retries a failed key probe twice before refusing, so a dropped
  connection to nuget.org no longer reads as a rejected key.

## [24.0.2] - 2026-10-14

No change to the packages' own source or public API: they are rebuilt on the dependency pins
below, and the build, the demos and the release tooling around them change.

### Changed

- **Build, test and pack go through Fallout targets in C#** (`eng/build`, `./eng/build.sh <Target>`,
  `./eng/build.sh --help`). They replace the 55 Python scripts under `tools/`, their
  `requirements.txt`, `tools/smoketest.sh`, the Makefile lanes and the mise tasks: one toolchain.
  `./eng/build.sh Check` is the gate before a push or a release (it was `mise run check`) and
  `./eng/build.sh CheckFull` runs everything (it was `mise run check-full`); `make <target>` and the
  two mise tasks are thin wrappers over them. The targets: `Build`, `Pack`, `Contracts`,
  `ApiBaseline`, `GeneratorSnapshots`, `PackageLayout`, `ConsumerBehavior`, `AotPublishGate`,
  `Demos`, `LiveCheck`, `Smoke`, `WebApiAotDemo`, `OtlpEvidence`, `Check`, `CheckFull` and
  `TestbedProbe`.
- **The demos are asserted through a local qyl collector.** Every demo exports OTLP to the collector
  the `qyl` dotnet tool ships (pinned in `.config/dotnet-tools.json`, started once per build run on
  free loopback ports) through `QYL_ENDPOINT`, and its lane asserts, besides the demo's own report,
  that every span, measurement series and log record the report names arrived with the reported
  name, kind, status and attribute values, read through the collector API typed by
  `Qyl.Api.Contracts`. Every span a demo report lists carries its `TraceId` and `SpanId`. The demos
  that wired no exporter (ADO.NET, SQLite, EF Core, Kafka, Redis, SqlClient, WCF client and the two
  metrics demos) export through `demos/Shared/QylDemoOtlp.cs`; the `AddQyl()` demos take its
  discovery path instead of a hard-coded endpoint.
- **The ten broker and database lanes use the Railway testbed instead of Docker.** They read
  `~/.config/vitals/qyl-testbed.env` (`QYL_TESTBED_ENV` overrides the path), wake a sleeping
  service with a protocol-level probe and a visible countdown for up to
  `QYL_TESTBED_WAKE_SECONDS_MAX`, and fail naming the service. The SqlClient lane runs on any
  machine now that SQL Server runs x64 on Railway; the Kafka demo speaks SASL/PLAIN when
  `QYL_KAFKA_SASL_USERNAME` and `QYL_KAFKA_SASL_PASSWORD` are set; the MongoDB demo empties its
  collection first, because a woken testbed keeps its data.
- The contracts cite the C# gates as evidence (`eng/build/Demos/Lanes/<Name>Lane.cs`,
  `eng/build/ConsumerBehavior/*.cs`), and the generated contract artifacts are rewritten by
  `./eng/build.sh Contracts --write-contracts`. Consumer fixtures live in `eng/fixtures`.
- `release.sh` packs through `./eng/build.sh Pack` into `artifacts/release`. Its dry run reports
  the branch, origin, tag and already-indexed checks as warnings instead of refusing, so it proves
  the pack from any branch, and it proves the NuGet key for every package id through NuGet's
  package-verification handshake instead of pushing an already-published package with
  `--skip-duplicate`: a dry run publishes nothing.
- Compile against `Qyl.Telemetry.SemanticConventions` 9.9.2 (from 9.9.0) and run the live check
  against the `v9.9.2` registry. 9.9.1 declared `qyl.gen_ai.tool.call.arguments_hash`, which qyl
  does not emit; 9.9.2 changed only how that repository builds and tests. The source generator
  embeds `ANcpLua.Roslyn.Utilities.Sources` 2.2.48 (from 2.2.47), and the build types the collector
  client with `Qyl.Api.Contracts` 12.1.2 (from 12.1.1, the same contract revision).
- Version sync requires the top CHANGELOG entry to be `## [<Version>] - YYYY-MM-DD` and rejects
  an `[Unreleased]` entry: a change is recorded under the version that ships it, bumping
  `<Version>` first when that version is already released.

### Fixed

- `bun eng/testbed/probe.ts` no longer prints a `TimeoutNegativeWarning`: kafkajs 2.2.4 schedules
  its request-queue check `throttledUntil - Date.now()` ms ahead, which is `-1 - Date.now()` while
  the queue is unthrottled. `eng/testbed/patches/kafkajs@2.2.4.patch` clamps the delay at 0.
- The ILogger demo exported no log record over OTLP: its first export ran inside the host's
  disposal against an already-disposed container. The gRPC client demo never flushed its batch
  exporter. Both were invisible while the demos asserted only their in-memory exporters.

Older entries: [CHANGELOG.archive.md](CHANGELOG.archive.md).
