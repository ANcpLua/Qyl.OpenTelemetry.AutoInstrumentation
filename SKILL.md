# Qyl.Telemetry.AutoInstrumentation: release, pins and agent hand-offs

Who reads this: the one agent working on this repository's `main`. Agent, user and owner are
the same word. There is one agent per repository, six in all (qyl, qyl.mcp, qyl-api-schema,
Qyl.Telemetry.AutoInstrumentation, Qyl.Telemetry.SemanticConventions, qyl.at), each on its own
`main`. Commit and push to `main` directly: no pull requests, no CI, no branch rules. The gate
is `mise run check`; packages are .NET only (`dotnet restore Qyl.Telemetry.AutoInstrumentation.slnx`, no JavaScript lockfile).

## This repository

- Publishes to NuGet every packable project under `src/`: `Qyl.Telemetry.AutoInstrumentation`,
  `.DiagnosticListeners`, `.EntityFrameworkCore`, `.Hosting`, `.SqlClient` and
  `Qyl.Telemetry.Hosting`. The release command derives the list from `src/`; a new packable
  project ships automatically, a project that must not ship sets `<IsPackable>false</IsPackable>`.
- Version source: `<Version>` in `Directory.Build.props`, bumped in its own commit together with
  the `## [x.y.z]` entry in `CHANGELOG.md`; the tag names that commit. The CHANGELOG entry
  becomes the GitHub release notes.
- Release command: `./release.sh <x.y.z> --dry-run`, then `./release.sh <x.y.z>` (`dotnet pack`
  per project, push, tag, GitHub release, wait for indexing). The script snapshots the packed
  commit, refuses tracked edits or a changed HEAD after packing, and tags that exact commit.
- `mise run check` is the complete local gate: it builds the solution and demos, checks contracts,
  package and API behavior, runs managed and NativeAOT consumers, the strict NativeAOT publish
  matrix, real-library Docker demos, live registry validation, smoke and OTLP checks. Run it
  before every push and release. Set `QYL_SEMCONV_REGISTRY` to the canonical SemanticConventions
  checkout at the pinned version after running its `scripts/fetch-core.sh`; Docker or OrbStack
  must be running. The goal verifier performs the build, so the task does not build twice.
  `mise install` provides dotnet and Weaver 0.27.0 (pinned in `mise.toml`); `make deps` installs
  the Python verifier requirements (including PyYAML). Run through mise so Weaver is on PATH.
- `mise exec -- make verify` runs the focused no-Docker subset for development, and `make all`
  adds the Docker demos and a fresh clone of the pinned live-check registry. Every lane in
  these targets is already included in a successful full `mise run check`; do not repeat the
  same coverage just to release. `make help` lists the individual lanes for focused reruns.
- Key: `vitals keys` entry "qyl-dev manual publish and release (NuGet)", file
  `~/.config/vitals/nuget-qyl-manual-publish-key`. The script reads it; never print, paste,
  commit or log a key. If the entry is missing or its note's expiry has passed, stop and tell
  the owner.
- Upstream pins this repo must keep fresh: `Qyl.Telemetry.SemanticConventions*` in
  `Directory.Packages.props`, and `SEMCONV_REF` in `tools/live-check.env`, which moves with it.
- Consumers to notify after all six packages are indexed: qyl (`Version.props`
  `QylTelemetryVersion`) and qyl.at (`src/data/site.ts`).

## Before any work: the stale-version guard

1. `git pull --ff-only origin main`.
2. `mise install`, then `mise exec -- dotnet restore Qyl.Telemetry.AutoInstrumentation.slnx`.
3. Compare the pinned `Qyl.Telemetry.SemanticConventions` version with the last entry of
   `curl -fsSL https://api.nuget.org/v3-flatcontainer/qyl.telemetry.semanticconventions/index.json`.
   If a newer version is indexed, re-pin first (section "When a release lands"), then start
   the actual work. Never build on a pin older than what is indexed.

## Releasing

1. Land the bump commit on `main`: `Directory.Build.props` `<Version>` and the CHANGELOG entry.
2. `./release.sh <x.y.z> --dry-run` only when a step is new or unsure; otherwise go straight to
   the release, the script refuses before it publishes when anything is off.
3. `./release.sh <x.y.z>`.
4. Watch indexing: the script polls the flat-container index of all six ids. If it ends with
   "not all indexed yet", keep polling every 30 s. Until all six are indexed, do nothing else
   about this release.
5. The moment all six are indexed: stop watching and notify the consumers (section "Notify").
6. Record the published version in
   `/Users/alexandernachtmann/Downloads/qyl-codex-claude/qyl-handoff-for-codex.md` and run
   `/Users/alexandernachtmann/Downloads/qyl-codex-claude/refresh-published-versions.sh`, which rewrites
   `qyl-published-versions.yaml` beside it from the registries (the snapshot every agent reads first).

## Notify

One message per consumer repo, to its running agent through the session's agent-messaging
tool when there is one (Claude Code: `ListAgents`, then `SendMessage`). If the consumer has
no running agent, append the same text to the handoff file above under "Pending re-pins";
the owner relays it. Text:

```
RELEASE Qyl.Telemetry.AutoInstrumentation* + Qyl.Telemetry.Hosting <version> indexed <UTC time>
repo: <qyl | qyl.at>
re-pin: <Version.props QylTelemetryVersion | src/data/site.ts>
then: regenerate the lockfile, run the gate, commit, push main
```

## When a release lands and you are the consumer

Act at once, before continuing your own work, so both agents keep moving:

1. Set the new `Qyl.Telemetry.SemanticConventions*` version in `Directory.Packages.props` and
   `SEMCONV_REF` in `tools/live-check.env`.
2. `mise exec -- dotnet restore Qyl.Telemetry.AutoInstrumentation.slnx` (add `--force-evaluate` where a `packages.lock.json` exists).
3. `mise run check`.
4. Commit `chore: pin Qyl.Telemetry.SemanticConventions <version>` and push `main` immediately.
5. If the pin changes what this repo ships, bump `<Version>`, add the CHANGELOG entry and
   continue with "Releasing": one release per repo per wave.

## Order rules that never change

- This repo and SemanticConventions both feed qyl: qyl re-pins both in one commit once both are
  indexed, so tell qyl's agent whether SemanticConventions moved in the same wave.
- qyl.at lists a version only after the index confirms it.
