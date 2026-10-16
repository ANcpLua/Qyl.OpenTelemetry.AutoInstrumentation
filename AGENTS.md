# Qyl.Telemetry.AutoInstrumentation

qyl's .NET instrumentation: Roslyn interceptors, public runtime hooks, native ActivitySource/Meter subscriptions, bootstrap and export orchestration.

Push to `main` directly. Gate before every push: `./eng/build.sh Check` (`mise run check`); it prints a summary and the log path under `artifacts/logs/`, `QYL_VERBOSE=1` (or `--verbose`) prints everything. CI runs the same gate and only reports (`.github/workflows/check.yml`, push and pull request). Releases, re-pins and consumers: [`SKILL.md`](SKILL.md).

## Ownership

- qyl owns the implementation. `contracts/otel-dotnet-auto.upstream.yaml` is a coverage comparison baseline, never an upstream agent dependency or a version to install. Read the ownership and generated coverage contracts before claiming support or a gap.
- `Qyl.Telemetry.SemanticConventions` supplies the convention bindings. The OpenTelemetry SDK, hosting integration and OTLP exporter stay implementation dependencies of `Qyl.Telemetry.Hosting` (replacing the upstream agent does not replace its SDK). Upgrade reports keep qyl's instrumentation and convention versions apart from the SDK and exporter versions.

## Build

- Fallout targets in `eng/build`: `./eng/build.sh <Target>`, listed by `./eng/build.sh --help`; `make <target>` wraps them. `mise install` provides dotnet and Weaver, which LiveCheck finds through mise.
- `./eng/build.sh CheckFull` adds the NativeAOT publish matrix, the thirty demo lanes, live check, smoke, the web API AOT demo and the OTLP wire evidence. Run it when a change touches AOT publishing, a real-library integration or the registry pin, never as a release precondition. Focused reruns: `Demos --lane <name>`, `LiveCheck`, `AotPublishGate`.
- The thirty native MSTest cases in `tests/Qyl.Telemetry.Integration.Tests/Lanes` run the real demo applications against per-execution `Qyl.Telemetry.Testing` OTLP receivers; seven additional cases verify producer conversions, real-host HTTP/gRPC export and invalid protocol configuration. Preserve full captures and registry reports, and compare them with independent application reports; do not select records by a clock window. The separate wire-evidence target checks delivery independently; Collector persistence belongs to qyl's integration suite. Consumer fixtures: `eng/fixtures`. `./eng/build.sh Contracts --write-contracts` rewrites the generated contract artifacts.

## Testbed

The demo verifiers' databases and brokers run in the Railway project `qyl-testbed` (`.railway/railway.ts`), never on this machine; README "Testbed on Railway". Connection strings: `~/.config/vitals/qyl-testbed.env` (Vitals "qyl testbed connection strings (Railway qyl-testbed)"). `bun eng/testbed/probe.ts` wakes and checks all eight services; `eng/testbed/provision.sh` recreates the project and rewrites that file.
