#!/usr/bin/env bash
# Publish and release the Qyl.Telemetry.AutoInstrumentation* and Qyl.Telemetry.Hosting packages from
# this machine with the key registered in Vitals. No workflow publishes. Packing is the Fallout target
# Pack (./eng/build.sh Pack); this script adds the release checks, the push, the tag and the release.
#
#   ./release.sh <x.y.z>            pack every packable src project, push, tag v<x.y.z>, GitHub release
#   ./release.sh <x.y.z> --dry-run  every check, the real pack and a NuGet key probe that publishes nothing.
#                                   Checks the real run enforces (main, origin/main, tag, already on
#                                   NuGet) only warn, so a dry run proves the pack from any branch.
#
# Version source: <Version> in Directory.Build.props with its CHANGELOG.md entry "## [x.y.z]" on
# top (the first change after a release bumps both); the tag names HEAD. The script never edits either.
set -euo pipefail
cd "$(dirname "$0")"

NUGET_SOURCE=https://api.nuget.org/v3/index.json
NUGET_FLAT=https://api.nuget.org/v3-flatcontainer
NUGET_GALLERY=https://www.nuget.org/api/v2
NUGET_KEY_FILE=$HOME/.config/vitals/nuget-qyl-manual-publish-key  # vitals keys: "qyl-dev manual publish and release (NuGet)"
OUT=artifacts/release  # where ./eng/build.sh Pack puts the packages

version=${1:-}
dry=0; [[ ${2:-} == --dry-run ]] && dry=1
[[ $version =~ ^[0-9]+\.[0-9]+\.[0-9]+$ ]] || { echo "usage: $0 <x.y.z> [--dry-run]" >&2; exit 2; }
say() { printf '\n== %s\n' "$*"; }
die() { echo "release: $*" >&2; exit 1; }
# A check the real run enforces; a dry run reports it and goes on.
refuse() { (( dry )) || die "$*"; echo "warning: $*; the real run refuses"; }
lower() { tr '[:upper:]' '[:lower:]' <<<"$1"; }
indexed() { curl -fsSL "$NUGET_FLAT/$(lower "$1")/index.json" 2>/dev/null | grep -q "\"$2\""; }
latest() { curl -fsSL "$NUGET_FLAT/$(lower "$1")/index.json" 2>/dev/null | grep -o '"[0-9][^"]*"' | tail -1 | tr -d '"'; }
# The key probe publishes nothing: it is NuGet's push-symbols handshake. The key mints a one-use
# package verification key for <id> <version> (only a live key with push scope can), and that key
# verifies with 200 only when the key's account owns <id>; the verify call consumes it. The key
# travels in a curl config on stdin, never on a command line.
key_probe() {
  local id=$1 version=$2 minted temp
  minted=$(printf 'header = "X-NuGet-ApiKey: %s"\n' "$(<"$NUGET_KEY_FILE")" |
    curl -fsS -X POST -H 'Content-Length: 0' -K - "$NUGET_GALLERY/package/create-verification-key/$id/$version") || return 1
  temp=$(sed -n 's/.*"Key":"\([^"]*\)".*/\1/p' <<<"$minted")
  [[ -n $temp ]] || return 1
  [[ $(printf 'header = "X-NuGet-ApiKey: %s"\n' "$temp" |
    curl -sS -o /dev/null -w '%{http_code}' -K - "$NUGET_GALLERY/verifykey/$id/$version") == 200 ]]
}

say "checks"
[[ -r $NUGET_KEY_FILE ]] || die "NuGet key missing: $NUGET_KEY_FILE (vitals keys)"
[[ $(git branch --show-current) == main ]] || refuse "not on main"
[[ -z $(git status --porcelain --untracked-files=no) ]] || die "working tree not clean"
git fetch -q origin main
release_commit=$(git rev-parse HEAD)
[[ $release_commit == $(git rev-parse origin/main) ]] || refuse "HEAD differs from origin/main"
assert_packed_commit() {
  [[ $(git rev-parse HEAD) == "$release_commit" ]] || die "HEAD changed while packing; nothing published"
  [[ -z $(git status --porcelain --untracked-files=no) ]] || die "tracked files changed while packing; nothing published"
}
if [[ -n $(git ls-remote --tags origin "refs/tags/v$version") ]] || git rev-parse -q --verify "refs/tags/v$version" >/dev/null; then
  refuse "tag v$version already exists"
fi
dv=$(sed -n 's:.*<Version>\([^<]*\)</Version>.*:\1:p' Directory.Build.props | head -1)
[[ $dv == "$version" ]] || die "Directory.Build.props <Version> is $dv, not $version; bump it and its CHANGELOG entry first"
grep -q "^## \[$version\]" CHANGELOG.md || die "CHANGELOG.md has no '## [$version]' entry"
gh auth status >/dev/null 2>&1 || die "gh is not logged in"

# Every packable project under src/ ships; the list is derived, never a literal (the Pack target derives it the same way).
projects=()
while IFS= read -r csproj; do
  grep -q '<IsPackable>false</IsPackable>' "$csproj" && continue
  projects+=("$csproj")
done < <(find src -mindepth 2 -maxdepth 2 -name '*.csproj' | sort)
(( ${#projects[@]} > 0 )) || die "no packable project under src/"
for p in "${projects[@]}"; do
  id=$(basename "$p" .csproj)
  if indexed "$id" "$version"; then refuse "$id $version is already on NuGet"; fi
done
echo "packages: ${#projects[@]}"

say "pack (./eng/build.sh Pack)"
./eng/build.sh Pack
for p in "${projects[@]}"; do
  package="$OUT/$(basename "$p" .csproj).$version.nupkg"
  [[ -f $package ]] || die "Pack did not produce $package"
done
count=$(find "$OUT" -maxdepth 1 -name '*.nupkg' | wc -l | tr -d ' ')
[[ $count == "${#projects[@]}" ]] || die "packed $count packages, expected ${#projects[@]}"
ls -1 "$OUT"/*.nupkg
assert_packed_commit

say "NuGet push"
notes=$(mktemp); trap 'rm -f "$notes"' EXIT
if (( dry )); then
  for p in "${projects[@]}"; do
    id=$(basename "$p" .csproj)
    published=$(latest "$id" || true)
    if [[ -z $published ]]; then
      echo "warning: $id is not on NuGet yet; the key probe needs a published version, so its first push is the probe"
      continue
    fi
    key_probe "$id" "$published" || die "the NuGet key failed the probe for $id: expired, revoked, without push scope, or its account does not own $id (vitals keys)"
    echo "key probe: $id ok (verified against $published, nothing published)"
  done
  say "dry run finished: ${#projects[@]} packages at $version in $OUT; nothing published, nothing tagged"
  git status --short --untracked-files=no
  exit 0
fi
for p in "$OUT"/*.nupkg; do
  dotnet nuget push "$p" --api-key "$(<"$NUGET_KEY_FILE")" --source "$NUGET_SOURCE"
done

say "tag and GitHub release (notes from CHANGELOG.md)"
awk -v v="$version" 'index($0, "## [" v "]") == 1 {on=1; next} on && /^## \[/ {exit} on' CHANGELOG.md >"$notes"
git tag -a "v$version" "$release_commit" -m "v$version"
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
