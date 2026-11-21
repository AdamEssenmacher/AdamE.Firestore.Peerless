#!/usr/bin/env bash
# Packs AdamE.Firestore.Peerless, AdamE.Firestore.Peerless.Android and AdamE.Firestore.Peerless.iOS into a local NuGet
# feed (a folder), so an app can use Peerless as packages before anything is published. Needs macOS with Xcode and the
# android and ios workloads: the platform packages carry the shims (the Android AAR, the iOS xcframework), rebuilt
# here from source. All three share one version, which an app pins once.
#
#   scripts/pack-local.sh [--macos] <feed directory> [version]
#
# --macos packs the core and macOS package without the iOS or Android workloads.
#
# The version defaults to 0.1.0-preview.g<commit>, so a package names the commit it was built from. A tree with
# uncommitted changes needs an explicit version. Run both modes from the same clean revision without a version
# argument to share the core package. Explicit versions are single-use because they do not identify the source
# revision. Platform packages cannot be overwritten because NuGet caches packages by version.
set -euo pipefail

usage="usage: $0 [--macos] <feed directory> [version]"
mac_only=false
if [[ ${1:-} == --macos ]]; then
  mac_only=true
  shift
fi
[[ $# -ge 1 && $# -le 2 ]] || { echo "$usage" >&2; exit 2; }
root="$(cd "$(dirname "$0")/.." && pwd)"
mkdir -p "$1"
feed="$(cd "$1" && pwd)"

commit_version=false
if [[ $# -eq 2 ]]; then
  version="$2"
else
  if [[ -n "$(git -C "$root" status --porcelain)" ]]; then
    echo "pack-local: the tree has uncommitted changes; commit them or pass a version" >&2
    exit 2
  fi
  version="0.1.0-preview.g$(git -C "$root" rev-parse --short=8 HEAD)"
  commit_version=true
fi

# NuGet caches a package by its version, so one already restored on this Mac would shadow the new build.
cache="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
lower_version="$(printf '%s' "$version" | tr '[:upper:]' '[:lower:]')"
if "$mac_only"; then
  projects=(AdamE.Firestore.Peerless AdamE.Firestore.Peerless.macOS)
else
  projects=(AdamE.Firestore.Peerless AdamE.Firestore.Peerless.Android AdamE.Firestore.Peerless.iOS)
fi
pending=()
for id in "${projects[@]}"; do
  lower_id="$(printf '%s' "$id" | tr '[:upper:]' '[:lower:]')"
  feed_package="$feed/$id.$version.nupkg"
  cache_dir="$cache/$lower_id/$lower_version"
  if "$commit_version" && [[ "$id" == AdamE.Firestore.Peerless && -f "$feed_package" ]]; then
    if [[ -e "$cache_dir" ]] && ! cmp -s "$feed_package" "$cache_dir/$lower_id.$lower_version.nupkg"; then
      echo "pack-local: $id $version in the NuGet cache does not match $feed_package" >&2
      exit 2
    fi
    continue
  fi
  if [[ -e "$feed_package" || -e "$cache_dir" ]]; then
    echo "pack-local: $id $version is already in $feed or the NuGet cache; pick another version" >&2
    exit 2
  fi
  pending+=("$id")
done

# Into a folder of its own first, so a failed pack leaves nothing in the feed.
out="$(mktemp -d)"
trap 'rm -rf "$out"' EXIT
# From the repository, so its global.json picks the SDK and workloads, wherever the script is run from.
cd "$root"
# A package must not carry a shim left over from another checkout state: each build target decides from timestamps,
# which a checkout doesn't order.
if ! "$mac_only"; then
  rm -rf ios/build/PeerlessShim.xcframework android/peerless-shim/build/outputs/aar
fi
for project in "${pending[@]}"; do
  dotnet pack "src/$project/$project.csproj" -c Release -p:Version="$version" -o "$out"
done
mv "$out"/*.nupkg "$feed"/
echo "pack-local: packed $version into $feed"
