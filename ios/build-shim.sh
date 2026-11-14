#!/usr/bin/env bash
# Builds ios/build/PeerlessShim.xcframework: a static library (simulator arm64 + x86_64, device arm64) compiled against
# the headers of the FirebaseFirestoreInternal and FirebaseCore frameworks that AdamE.Firebase.iOS.CloudFirestore
# ships. Nothing of Firestore is linked in: the app resolves those symbols against the binding's own frameworks, so
# it carries one copy of Firestore.
#
# The shim is compiled against one Firebase version (recorded in the library; .NET refuses a different major version at
# run time). The device (ios-arm64) slice is built but has not been run on a device.
#
# Usage: ios/build-shim.sh [binding version]   (default: the version in Directory.Packages.props)
set -euo pipefail

root="$(cd "$(dirname "$0")/.." && pwd)"
shim="$root/ios/PeerlessShim"
out="$root/ios/build"
version="${1:-$(sed -n 's/.*"AdamE.Firebase.iOS.CloudFirestore" Version="\([^"]*\)".*/\1/p' "$root/Directory.Packages.props")}"
packages="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
min_ios=15.0

# The binding's frameworks, headers included (an app bundle keeps only the binaries). The package's lib directory is
# found rather than assumed (it is named after the binding's own target framework and iOS SDK, e.g. net10.0-ios26.0).
headers="$out/firebase-$version"
if [[ ! -f "$headers/.complete" ]]; then
  rm -rf "$headers" && mkdir -p "$headers"
  for package in cloudfirestore:Firebase.CloudFirestore:FirebaseFirestoreInternal core:Firebase.Core:FirebaseCore; do
    IFS=: read -r id assembly framework <<<"$package"
    dir="$packages/adame.firebase.ios.$id/$version"
    zip="$(ls -d "$dir"/lib/net*-ios*/"$assembly".resources.zip 2>/dev/null | sort -V | tail -1 || true)"
    [[ -n "$zip" ]] || { echo "No $assembly.resources.zip under $dir/lib/net*-ios* (restore the iOS projects first)" >&2; exit 1; }
    unzip -q -o "$zip" "$framework.xcframework/*" -d "$headers"
    [[ -d "$headers/$framework.xcframework" ]] || { echo "$zip has no $framework.xcframework" >&2; exit 1; }
  done
  touch "$headers/.complete"
fi

# -fobjc-arc-exceptions: the writer throws on a value it refuses (and the listener catches it), so ARC must release the
# strong references held by the frames that exception unwinds through, such as the snapshot being encoded.
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
        -fobjc-arc -fobjc-arc-exceptions -fmodules -O2 -g -Wall -Wextra -Werror -Wno-unused-parameter \
        -DADAME_PEERLESS_FIREBASE_VERSION="\"$version\"" \
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
