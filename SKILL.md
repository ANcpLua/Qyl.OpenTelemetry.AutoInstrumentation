# Qyl.Telemetry.AutoInstrumentation: release and pins

One agent per qyl-dev repo (qyl, qyl.mcp, qyl-api-schema, Qyl.Telemetry.AutoInstrumentation, Qyl.Telemetry.SemanticConventions, qyl.at), each on its own `main`; agent, user and owner are one word. No PR and no publishing workflow; CI only reports: releases run on this machine with the keys in Vitals (`vitals keys`).

## This repository

- Publishes every packable `src/` project to NuGet (`Qyl.Telemetry.AutoInstrumentation`, `.DiagnosticListeners`, `.EntityFrameworkCore`, `.Hosting`, `.SqlClient`, `Qyl.Telemetry.Hosting`). A new packable project ships automatically; one that must not ship sets `<IsPackable>false</IsPackable>`.
- Version source: `<Version>` in `Directory.Build.props`. The first change after a release bumps it and opens its `## [x.y.z] - YYYY-MM-DD` entry on top of `CHANGELOG.md` in the same commit; later changes add to that entry. The entry becomes the GitHub release notes.
- Bump: Check rewrites `src/*/PublicAPI.txt` to the public API of the six packages and notes `api: +N -M changed` or `api: unchanged`; it informs, never blocks. Read `git diff v<last release> -- 'src/*/PublicAPI.txt'`: a removed or changed line is major, only added lines minor, none patch.
- Key: Vitals entry "qyl-dev manual publish and release (NuGet)", `~/.config/vitals/nuget-qyl-manual-publish-key`, read by the script. Never print, paste, commit or log it. Entry missing or note expired: stop and tell the owner.
- Upstream pin: `Qyl.Telemetry.SemanticConventions*` in `Directory.Packages.props`, the one place that names the release; the live check's registry tag derives from it.

## Stale-version guard, before any work

1. `git pull --ff-only origin main`, `mise install`, `mise exec -- dotnet restore Qyl.Telemetry.AutoInstrumentation.slnx`.
2. Compare the SemanticConventions pin with the last entry of `curl -fsSL https://api.nuget.org/v3-flatcontainer/qyl.telemetry.semanticconventions/index.json`; if the index is ahead, re-pin first.

## Release

1. `main` holds `<Version>` with its CHANGELOG entry on top, gate green, pushed.
2. `./release.sh <x.y.z>` (`--dry-run` first only when a step is new or unsure; the script refuses before publishing when anything is off). It returns before indexing, once NuGet accepted the push and the tag and GitHub release exist; a detached job runs `./eng/build.sh VerifyRelease --published-version <x.y.z>` (index wait for all six, the package smoke against nuget.org) and then the snapshot refresh (`published-versions.yaml` in qyl.at), logging to `artifacts/release-<x.y.z>-verify.log`. Do not run the snapshot refresh by hand.
3. `./release.sh --wait <x.y.z>` blocks until that log ends in VERIFIED or FAILED; nothing else about this release until then. Exit 3 = not indexed in time, `./release.sh --verify <x.y.z>` checks again; any other FAILED after publication is a patch release.
4. VERIFIED: notify the consumers.

## Notify

| Consumer | Re-pins |
|---|---|
| qyl | `Version.props` `QylTelemetryVersion` |
| qyl.at | `src/data/site.ts` |

One message per consumer through the session's agent-messaging tool (Claude Code: `ListAgents`, then `SendMessage`):

```
RELEASE Qyl.Telemetry.AutoInstrumentation* + Qyl.Telemetry.Hosting <version> indexed <UTC time>
repo: <qyl | qyl.at>
re-pin: <Version.props QylTelemetryVersion | src/data/site.ts>
then: regenerate the lockfile, run the gate, commit, push main
```

## Re-pin when SemanticConventions releases

Act at once, before your own work:

1. Set the new version in `Directory.Packages.props`.
2. `mise exec -- dotnet restore Qyl.Telemetry.AutoInstrumentation.slnx` (`--force-evaluate` where a `packages.lock.json` exists). NU1102 for a version the index lists is NuGet's 30-minute HTTP cache: delete that id's `list_<id>.dat` under `dotnet nuget locals http-cache --list`, restore again.
3. `./eng/build.sh Check`; commit `chore: pin Qyl.Telemetry.SemanticConventions <version>`; push `main`.
4. If the pin changes what ships, record it under the next `<Version>` (bump it if the current one is released) and release: one release per repo per wave.

This repo and SemanticConventions both feed qyl, which re-pins both in one commit once both are indexed: tell qyl's agent whether SemanticConventions moved in the same wave.
