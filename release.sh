#!/usr/bin/env bash
# Publish and release the Qyl.Telemetry.AutoInstrumentation* and Qyl.Telemetry.Hosting
# packages from this machine with the key registered in Vitals. No workflow publishes.
#
#   ./release.sh <x.y.z>            pack every packable src project, push, tag v<x.y.z>, GitHub release
#   ./release.sh <x.y.z> --dry-run  every check, the real pack and a NuGet key probe; nothing changes
#
# Version source: <Version> in Directory.Build.props, bumped in its own commit together with
# the CHANGELOG.md entry "## [x.y.z]"; the tag names HEAD. The script never edits either.
set -euo pipefail
cd "$(dirname "$0")"

NUGET_SOURCE=https://api.nuget.org/v3/index.json
NUGET_FLAT=https://api.nuget.org/v3-flatcontainer
NUGET_KEY_FILE=$HOME/.config/vitals/nuget-qyl-manual-publish-key  # vitals keys: "qyl-dev manual publish and release (NuGet)"
OUT=artifacts/release

version=${1:-}
dry=0; [[ ${2:-} == --dry-run ]] && dry=1
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "usage: $0 <x.y.z> [--dry-run]" >&2; exit 2; }
say() { printf '\n== %s\n' "$*"; }
die() { echo "release: $*" >&2; exit 1; }
lower() { tr '[:upper:]' '[:lower:]' <<<"$1"; }
indexed() { curl -fsSL "$NUGET_FLAT/$(lower "$1")/index.json" 2>/dev/null | grep -q "\"$2\""; }

say "checks"
[[ -r $NUGET_KEY_FILE ]] || die "NuGet key missing: $NUGET_KEY_FILE (vitals keys)"
[[ $(git branch --show-current) == main ]] || die "not on main"
[[ -z $(git status --porcelain --untracked-files=no) ]] || die "working tree not clean"
git fetch -q origin main
[[ $(git rev-parse HEAD) == $(git rev-parse origin/main) ]] || die "main differs from origin/main"
if [[ -n $(git ls-remote --tags origin "refs/tags/v$version") ]] || git rev-parse -q --verify "refs/tags/v$version" >/dev/null; then
  (( dry )) || die "tag v$version already exists"; echo "warning: tag v$version already exists; the real run refuses"
fi
dv=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -1)
[[ $dv == "$version" ]] || die "Directory.Build.props <Version> is $dv, not $version; bump it in its own commit first"
grep -q "^## \[$version\]" CHANGELOG.md || die "CHANGELOG.md has no '## [$version]' entry"
gh auth status >/dev/null 2>&1 || die "gh is not logged in"

# Every packable project under src/ ships; the list is derived, never a literal.
projects=()
while IFS= read -r csproj; do
  grep -q '<IsPackable>false</IsPackable>' "$csproj" && continue
  projects+=("$csproj")
done < <(find src -mindepth 2 -maxdepth 2 -name '*.csproj' | sort)
(( ${#projects[@]} > 0 )) || die "no packable project under src/"
for p in "${projects[@]}"; do
  id=$(basename "$p" .csproj)
  if indexed "$id" "$version"; then (( dry )) || die "$id $version is already on NuGet"; echo "warning: $id $version is already on NuGet; the real run refuses"; fi
done
echo "packages: ${#projects[@]}"

say "pack"
rm -rf "$OUT"; mkdir -p "$OUT"
for p in "${projects[@]}"; do
  dotnet pack "$p" -c Release -o "$OUT" -p:Version="$version"
done
count=$(find "$OUT" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')
[[ $count == "${#projects[@]}" ]] || die "packed $count packages, expected ${#projects[@]}"

say "NuGet push"
probe_dir=$(mktemp -d); notes=$(mktemp); trap 'rm -rf "$probe_dir" "$notes"' EXIT
if (( dry )); then
  id=$(lower "$(basename "${projects[0]}" .csproj)")
  latest=$(curl -fsSL "$NUGET_FLAT/$id/index.json" | python3 -I -c 'import json,sys; print(json.load(sys.stdin)["versions"][-1])')
  curl -fsSL -o "$probe_dir/probe.nupkg" "$NUGET_FLAT/$id/$latest/$id.$latest.nupkg"
  echo "dry run: pushing the published $id $latest again with --skip-duplicate to prove the key (expect 'already exists')"
  dotnet nuget push "$probe_dir/probe.nupkg" --api-key "$(<"$NUGET_KEY_FILE")" --source "$NUGET_SOURCE" --skip-duplicate
  say "dry run finished: nothing published, nothing tagged"
  git status --short --untracked-files=no
  exit 0
fi
for p in "$OUT"/*.nupkg; do
  dotnet nuget push "$p" --api-key "$(<"$NUGET_KEY_FILE")" --source "$NUGET_SOURCE"
done

say "tag and GitHub release (notes from CHANGELOG.md)"
awk -v v="$version" 'index($0, "## [" v "]") == 1 {on=1; next} on && /^## \[/ {exit} on' CHANGELOG.md >"$notes"
git tag -a "v$version" -m "v$version"
git push -q origin "v$version"
gh release create "v$version" --title "v$version" --notes-file "$notes" "$OUT"/*.nupkg

say "verify indexing"
for _ in $(seq 1 20); do
  all=1
  for p in "${projects[@]}"; do indexed "$(basename "$p" .csproj)" "$version" || all=0; done
  (( all )) && { echo "all ${#projects[@]} packages indexed at $version"; break; }
  sleep 30
done
(( all )) || echo "pushed, not all indexed yet; re-check $NUGET_FLAT/<id>/index.json"

say "next (SKILL.md): once indexed, notify qyl (Version.props QylTelemetryVersion) and qyl.at (src/data/site.ts)"
