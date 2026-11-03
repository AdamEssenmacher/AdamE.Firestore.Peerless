#!/usr/bin/env bash
# Builds ios/build/PeerlessShim.xcframework: a static library (simulator arm64 + x86_64, device arm64) compiled against
# the headers of the FirebaseFirestoreInternal and FirebaseCore frameworks that AdamE.Firebase.iOS.CloudFirestore
# ships. Nothing of Firestore is linked in: the app resolves those symbols against the binding's own frameworks, so
# it carries one copy of Firestore.
#
# Usage: ios/build-shim.sh [binding version]   (default: the version in Directory.Packages.props)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
shim="$root/ios/PeerlessShim"
out="$root/ios/build"
version="${1:-$(sed -n 's/.*"AdamE.Firebase.iOS.CloudFirestore" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")}"
packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
min_ios=15.0

# The binding's frameworks, headers included (an app bundle keeps only the binaries).
headers="$out/firebase-$version"
if [[ ! -d "$headers/FirebaseFirestoreInternal.xcframework" ]]; then
  rm -rf "$headers" && mkdir -p "$headers"
  for package in cloudfirestore:Firebase.CloudFirestore core:Firebase.Core; do
    zip="$packages/adame.firebase.ios.${package%%:*}/$version/lib/net10.0-ios26.0/${package##*:}.resources.zip"
    [[ -f "$zip" ]] || { echo "missing $zip (restore the benchmark first)" >&2; exit 1; }
    unzip -q -o "$zip" 'FirebaseFirestoreInternal.xcframework/*' 'FirebaseCore.xcframework/*' -d "$headers" 2>/dev/null || true
  done
fi

compile() { # sdk slice target...
  local sdk="$1" slice="$2"; shift 2
  local sysroot; sysroot="$(xcrun --sdk "$sdk" --show-sdk-path)"
  local objects=()
  for target in "$@"; do
    local dir="$out/obj/$sdk-${target%%-*}"
    mkdir -p "$dir"
    for source in "$shim"/src/*.m; do
      local object="$dir/$(basename "${source%.m}").o"
      xcrun --sdk "$sdk" clang -c "$source" -o "$object" -target "$target" -isysroot "$sysroot" \
        -fobjc-arc -fmodules -O2 -g -Wall -Wextra -Werror -Wno-unused-parameter \
        -I "$shim/include" \
        -F "$headers/FirebaseFirestoreInternal.xcframework/$slice" -F "$headers/FirebaseCore.xcframework/$slice"
      objects+=("$object")
    done
  done
  local library="$out/lib/$sdk/libPeerlessShim.a"
  mkdir -p "$(dirname "$library")"
  rm -f "$library"
  if (($# == 1)); then
    xcrun libtool -static -o "$library" "${objects[@]}"
  else
    local parts=()
    for target in "$@"; do
      local part="$out/lib/$sdk/libPeerlessShim-${target%%-*}.a"
      xcrun libtool -static -o "$part" "$out/obj/$sdk-${target%%-*}"/*.o
      parts+=("$part")
    done
    xcrun lipo -create "${parts[@]}" -output "$library"
    rm -f "${parts[@]}"
  fi
}

compile iphonesimulator ios-arm64_x86_64-simulator "arm64-apple-ios$min_ios-simulator" "x86_64-apple-ios$min_ios-simulator"
compile iphoneos ios-arm64 "arm64-apple-ios$min_ios"

public="$out/headers"
rm -rf "$public" && mkdir -p "$public" && cp "$shim/include/PeerlessShim.h" "$public/"
rm -rf "$out/PeerlessShim.xcframework"
xcodebuild -create-xcframework \
  -library "$out/lib/iphonesimulator/libPeerlessShim.a" -headers "$public" \
  -library "$out/lib/iphoneos/libPeerlessShim.a" -headers "$public" \
  -output "$out/PeerlessShim.xcframework" >/dev/null
echo "built $out/PeerlessShim.xcframework against FirebaseFirestoreInternal $version"
