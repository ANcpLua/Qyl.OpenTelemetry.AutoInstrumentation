---
name: qyl-selfhosted-ci
description: Operate, repair, or recreate the self-hosted GitHub Actions CI for this repo (qyl-macos launchd runner on the dev Mac + qyl-linux systemd runner in the OrbStack machine qyl-ci). Use when CI jobs queue forever, a runner shows offline, a workflow needs a new runner label, the qyl-ci machine is gone, Docker images for the real-demo verifiers are missing, or when setting up the same pattern on a new machine or repo.
---

# qyl self-hosted CI

GitHub-hosted runners are unusable here: the repo is **private** (must stay private until the
history overhaul — never flip visibility) and the Actions minute quota is exhausted. Jobs on
`ubuntu-latest`/`macos-latest` die at setup with zero steps and no logs. Self-hosted runners
consume **no Actions minutes**, so all workflows run on the two runners below. Everything else
(checks UI, PR gates, `gh run` tooling, OIDC trusted publishing) works unchanged.

## Topology

| Runner | Label (`runs-on`) | Where | Service |
|---|---|---|---|
| `qyl-macos` | `qyl-macos` | dev Mac (arm64), `~/actions-runners/qyl-macos` | launchd: `actions.runner.ANcpLua-qyl-dotnet-autoinstrumentation.qyl-macos` (plist in `~/Library/LaunchAgents/`) |
| `qyl-linux` | `qyl-linux` | OrbStack Ubuntu machine `qyl-ci` (arm64), `~/actions-runner` | systemd: `actions.runner.ANcpLua-qyl-dotnet-autoinstrumentation.qyl-linux` |

Both legs are **arm64** (GitHub-hosted ubuntu was x64). The `qyl-ci` machine has its own native
Docker engine (`docker.io` package) because OrbStack does **not** share the host Docker socket
into machines — and the verifiers' `127.0.0.1` port publishing requires a local engine.
NativeAOT toolchain installed there: `clang`, `zlib1g-dev`, `libicu-dev`.

Workflows use `runs-on: ${{ matrix.os }}` with `os: [qyl-linux, qyl-macos]`.

## Health check

```bash
gh api repos/ANcpLua/qyl-dotnet-autoinstrumentation/actions/runners \
  --jq '.runners[] | "\(.name) \(.status)"'
```

Both must be `online`. If `qyl-linux` is offline, OrbStack or the machine is down:

```bash
open -a OrbStack            # daemon not running → docker.sock missing, `orb list` errors
orb start qyl-ci            # machine stopped
orb -m qyl-ci sudo systemctl restart actions.runner.ANcpLua-qyl-dotnet-autoinstrumentation.qyl-linux
```

If `qyl-macos` is offline:

```bash
cd ~/actions-runners/qyl-macos && ./svc.sh status || (./svc.sh stop; ./svc.sh start)
```

## Recreate from scratch

Registration tokens come from the API (valid ~1h, requires repo admin):

```bash
TOKEN=$(gh api -X POST repos/ANcpLua/qyl-dotnet-autoinstrumentation/actions/runners/registration-token --jq .token)
```

Latest runner version: `gh api repos/actions/runner/releases/latest --jq .tag_name` (v2.335.1
at setup time). The tarball ships only `config.sh`; `svc.sh` appears **after** configuration.

**macOS runner:**

```bash
mkdir -p ~/actions-runners/qyl-macos && cd ~/actions-runners/qyl-macos
curl -sL https://github.com/actions/runner/releases/download/v2.335.1/actions-runner-osx-arm64-2.335.1.tar.gz | tar xz
./config.sh --url https://github.com/ANcpLua/qyl-dotnet-autoinstrumentation \
  --token "$TOKEN" --name qyl-macos --labels qyl-macos --work _work --unattended
./svc.sh install && ./svc.sh start
```

**Linux runner (OrbStack machine):**

```bash
orb create ubuntu qyl-ci
orb -m qyl-ci sudo bash -c 'apt-get update -q && apt-get install -yq \
  clang zlib1g-dev libicu-dev git curl python3 ca-certificates jq docker.io \
  && usermod -aG docker ancplua && systemctl enable --now docker'
orb -m qyl-ci bash -c "mkdir -p ~/actions-runner && cd ~/actions-runner \
  && curl -sL https://github.com/actions/runner/releases/download/v2.335.1/actions-runner-linux-arm64-2.335.1.tar.gz | tar xz \
  && ./config.sh --url https://github.com/ANcpLua/qyl-dotnet-autoinstrumentation \
     --token $TOKEN --name qyl-linux --labels qyl-linux --work _work --unattended"
orb -m qyl-ci bash -c "cd ~/actions-runner && sudo ./svc.sh install ancplua && sudo ./svc.sh start"
orb -m qyl-ci bash -c 'echo "DOTNET_INSTALL_DIR=/home/ancplua/.dotnet" >> ~/actions-runner/.env \
  && sudo systemctl restart actions.runner.ANcpLua-qyl-dotnet-autoinstrumentation.qyl-linux'
```

The `.env` line is mandatory: on self-hosted Linux, `actions/setup-dotnet` defaults to
`/usr/share/dotnet` and dies with `mkdir: Permission denied`. Pointing it at the user home
also persists the SDK across runs (no re-download). macOS needs no override.

## Resource discipline (why downloads don't repeat)

- `actions/setup-dotnet` caches the SDK in the runner's persistent tool cache — downloaded
  once per runner, not per run. Same for the NuGet cache (`~/.nuget`).
- Container images for the real-demo verifiers are pinned by the verifiers themselves
  (env-overridable constants): `redis:8-alpine`, `rabbitmq:4.1-alpine`, `mongo:8-noble`,
  `apache/kafka:4.1.0`. They are pre-pulled in **both** engines (host OrbStack + qyl-ci).
  Never `docker pull` an unpinned `latest`; never keep duplicate tags of the same image.
- Host Docker also keeps `mcr.microsoft.com/dotnet/sdk:10.0-noble-aot` (qyl AOT container
  builds, referenced outside this repo).
- Cleanup that is always safe: `docker volume prune -f` (anonymous volumes only on this
  Docker version) and removing duplicate tags. The named volumes `paperless_*` and
  `bsc_texlive-cache` hold real data — leave them.

## OrbStack on demand (host performance)

OrbStack VMs cause noticeable lag on the host, so machines stay **stopped when CI is idle**:

```bash
orb start qyl-ci    # BEFORE pushing anything that triggers CI
orb stop qyl-ci     # when done
```

While `qyl-ci` is stopped, `qyl-linux` shows offline and Linux jobs **queue** (they don't
fail) until the machine is back. `qyl-macos` is unaffected. Quitting the OrbStack app
entirely also kills the host Docker engine (it IS the docker daemon) — fine when nothing
needs docker, recoverable with `open -a OrbStack`. The machine `otelvm` is unrelated to
this repo; it was found idle and stopped on 2026-06-12.

## Going public (the transition checklist)

The repo goes public only after the deliberate history overhaul, and only by Alex's hand.
Order matters:

1. **Rewrite history first** — semantically compacted commits; strip internal-only docs and
   notes. Full-history gitleaks scan on 2026-06-12 found zero real secrets (4 false
   positives: the YAML contract key `signals.logs.LOG4NET`). Re-run before the flip:
   `gitleaks git --no-banner .`
2. **Decommission both self-hosted runners BEFORE the visibility change** — on a public
   repo, fork PRs could execute arbitrary code on the dev Mac:
   `cd ~/actions-runners/qyl-macos && ./svc.sh stop && ./svc.sh uninstall && ./config.sh remove --token <removal-token>`
   (same inside `qyl-ci`, then `orb delete qyl-ci`).
3. **Switch workflows back to `ubuntu-latest`/`macos-latest`** — public repos get unlimited
   free GitHub-hosted minutes, so the self-hosted setup becomes unnecessary the moment the
   repo is public. Drop the `DOTNET_INSTALL_DIR` note with it.
4. Review README/AGENTS.md for internal-only constraints before they ship.
5. Flip visibility (human action), then re-enable trusted publishing checks.

## Security and constraints

- Self-hosted runners are safe here **only because the repo is private and solo**. If the
  repo ever goes public, fork PRs could execute arbitrary code on these machines — runners
  must be removed or replaced with ephemeral ones BEFORE any visibility change.
- The repo must stay private until the deliberate history overhaul; never change visibility
  to fix CI.
- `gh` billing endpoints need the `user` scope the stored token lacks — quota numbers are
  not readable programmatically; not needed since self-hosted runs are unmetered.
- Both runners are arm64. If a fixture ever diverges by architecture, suspect this first
  (GitHub-hosted ubuntu was x64).
