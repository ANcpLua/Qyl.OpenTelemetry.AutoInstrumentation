# Qyl.Telemetry.AutoInstrumentation

Release and re-pin: follow [`SKILL.md`](SKILL.md) at this repo root (canonical clone `/Users/alexandernachtmann/RiderProjects/Qyl.Telemetry.AutoInstrumentation/SKILL.md`); the one command is `./release.sh`. Published versions are read from the registry (nuget.org) or `Directory.Build.props` `<Version>`.

## Ownership and upstream versions

qyl owns the instrumentation implementation: Roslyn interceptors, public runtime hooks, native ActivitySource/Meter subscriptions, bootstrap and export orchestration. `contracts/otel-dotnet-auto.upstream.yaml` is a comparison baseline for coverage, not an upstream agent dependency or a version to install into qyl. Read the ownership and generated coverage contracts before claiming support or a gap.

`Qyl.Telemetry.SemanticConventions` supplies qyl's directly generated convention bindings. The OpenTelemetry SDK, hosting integration and OTLP exporter remain implementation dependencies of `Qyl.Telemetry.Hosting`; they do not make qyl an installation of the upstream .NET automatic-instrumentation agent. Distinguish qyl's instrumentation and convention versions from the SDK/exporter versions in every upgrade report. Do not remove SDK/exporter dependencies on the assumption that replacing the upstream agent replaces its separate SDK.

## Publishing across the qyl-dev repos

Push to `main` directly; no pull request, review, approval or CI exists. The only gate is the repo's local command named in SKILL.md, run before the push.

A release is done from the developer's machine with the registry API keys registered in Vitals (`vitals keys`): bump the version source, pack, push the packages to the registry, then tag `v<version>` on the released commit and create the GitHub release. No workflow publishes.

| Repo | Version source | Publishes |
|---|---|---|
| qyl-api-schema | release tag (one version for npm and NuGet); `package.json` stays `0.0.0-development` on `main` | `@ancplua/qyl-api-schema` (npm), `Qyl.Api.Contracts` (NuGet) |
| SemanticConventions | `Directory.Build.props` `VersionPrefix`, `registry/manifest.yaml` `schema_url` and a `CHANGELOG.md` entry; `scripts/generate.sh` must agree; `qyl.at/schemas/<version>` is served first | the three `Qyl.Telemetry.SemanticConventions*` packages (NuGet) and `@ancplua/qyl-semantic-conventions` (npm), all at one version |
| AutoInstrumentation | `Directory.Build.props` `Version` | the six `Qyl.Telemetry.*` packages (NuGet) |
| qyl | `Version.props` `QylVersion` | `qyl`, `qyl.<rid>` ×6, `Qyl.Api.Sdk` (NuGet) |
| qyl.mcp | `server/package.json` `version` plus a `server/README.md` release-notes entry | `qyl-mcp-server` (npm) |
| qyl.at | none | `./release.sh` deploys `qyl.at` manually |

Downstream pins move in the change that consumes the publish: qyl-api-schema → qyl (`Version.props` `QylApiContractsVersion`, `services/qyl.dashboard/package.json`, `bun.lock`, the README table) → qyl.mcp (`server`, `workbench` and `dashboard` `package.json`, `bun.lock`); SemanticConventions → AutoInstrumentation (`Directory.Packages.props`, `SEMCONV_REF`) and qyl (`QylSemanticConventionsVersion`), with qyl.at serving `public/schemas/<version>`; AutoInstrumentation → qyl (`QylTelemetryVersion`); every producer → qyl.at `src/data/site.ts`. One production rule stays: when the contract revision changes, push the Collector (qyl `main`) first, wait until `api.qyl.at/health` serves the new revision, then push qyl.mcp; `mcp.qyl.at` refuses a Collector on another revision.

Pushing `main` also deploys qyl → `qyl-collector` (`api.qyl.at`, Railway) and qyl.mcp → `qyl-mcp` (`mcp.qyl.at`, Railway). qyl.at deploys manually through `./release.sh` to the `qyl-at` Worker (`qyl.at`, Cloudflare).

## Build

Build, test and pack through the Fallout targets in `eng/build` (one toolchain, C#): `./eng/build.sh <Target>`, listed with their parameters by `./eng/build.sh --help`; `make <target>` wraps them. `./eng/build.sh Check` is the gate SKILL.md names; `./eng/build.sh CheckFull` adds the NativeAOT publish matrix, the thirty demo lanes, the live check, smoke, the web API AOT demo and the OTLP wire evidence. A demo lane exports OTLP to a local qyl collector (the `qyl` tool pinned in `.config/dotnet-tools.json`) and asserts the arrived telemetry through its API, typed by `Qyl.Api.Contracts`. Consumer fixtures live in `eng/fixtures`; the generated contract artifacts are rewritten by `./eng/build.sh Contracts --write-contracts`.

## Testbed

The demo verifiers' databases and brokers run in the Railway project `qyl-testbed`, declared in `.railway/railway.ts`, never on the developer machine: see the README section "Testbed on Railway". Their connection strings are in `~/.config/vitals/qyl-testbed.env` (Vitals entry "qyl testbed connection strings (Railway qyl-testbed)"); `bun eng/testbed/probe.ts` wakes and checks all eight services, and `eng/testbed/provision.sh` recreates the project from the partial and rewrites that file.
