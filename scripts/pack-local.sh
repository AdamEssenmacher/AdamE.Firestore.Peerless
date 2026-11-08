#!/usr/bin/env bash
# Packs AdamE.Firestore.Peerless and AdamE.Firestore.Peerless.iOS into a local NuGet feed (a folder), so an app can use
# Peerless as packages before anything is published. Needs macOS with Xcode and the ios workload: the iOS package
# carries the shim xcframework, rebuilt here from source.
#
#   scripts/pack-local.sh <feed directory> [version]
#
# The version defaults to 0.1.0-preview.g<commit>, so a package names the commit it was built from. A tree with
# uncommitted changes needs an explicit version, which keeps that promise. A version already in the feed is refused:
# NuGet caches packages by version, so replacing one would leave every consumer on the old bits.
set -euo pipefail

usage="usage: $0 <feed directory> [version]"
[[ $# -ge 1 && $# -le 2 ]] || { echo "$usage" >&2; exit 2; }
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$1"
feed="$(cd "$1" && pwd)"

if [[ $# -eq 2 ]]; then
  version="$2"
else
  if [[ -n "$(git -C "$root" status --porcelain)" ]]; then
    echo "pack-local: the tree has uncommitted changes; commit them or pass a version" >&2
    exit 2
  fi
  version="0.1.0-preview.g$(git -C "$root" rev-parse --short=8 HEAD)"
fi

# NuGet caches a package by its version, so one already restored on this Mac would shadow the new build.
cache="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
lower_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
for id in AdamE.Firestore.Peerless AdamE.Firestore.Peerless.iOS; do
  lower_id="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
  if [[ -e "$feed/$id.$version.nupkg" || -e "$cache/$lower_id/$lower_version" ]]; then
    echo "pack-local: $id $version is already in $feed or the NuGet cache; pick another version" >&2
    exit 2
  fi
done

# Into a folder of its own first, so a failed pack leaves nothing in the feed.
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
# From the repository, so its global.json picks the SDK and workloads, wherever the script is run from.
cd "$root"
# A package must not carry a shim left over from another checkout state.
rm -rf ios/build/PeerlessShim.xcframework
for project in AdamE.Firestore.Peerless AdamE.Firestore.Peerless.iOS; do
  dotnet pack "src/$project/$project.csproj" -c Release -p:Version="$version" -o "$out"
done
mv "$out"/*.nupkg "$feed"/
echo "pack-local: packed $version into $feed"
