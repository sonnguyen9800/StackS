#!/usr/bin/env bash
# Build the StackS iOS app (device: ios-arm64; simulator: pass "sim" for iossimulator-arm64).
# Needs: macOS, Xcode 15+, .NET 9+ SDK (NativeAOT on iOS is supported from .NET 9), CMake 3.24+.
set -euo pipefail
cd "$(dirname "$0")/.."
RID=ios-arm64; [ "${1:-}" = "sim" ] && RID=iossimulator-arm64

echo "== 1/3 C# game -> stacks_game.a (NativeAOT, $RID)"
dotnet publish src/StackS.Mobile -c Release -r "$RID" -p:PublishAot=true -p:NativeLib=Static -o "build/ios-nativeaot/$RID"

echo "== 2/3 collect static libraries"
OUT=ios/nativeaot; rm -rf "$OUT"; mkdir -p "$OUT"
cp "build/ios-nativeaot/$RID/stacks_game.a" "$OUT/"
PKGS="${NUGET_PACKAGES:-$HOME/.nuget/packages}"
# The NativeAOT runtime (GC, bootstrapper) and the native parts of the base library.
ILC_NATIVE=$(dirname "$(find "$PKGS" -path "*$RID*" -name 'libRuntime.WorkstationGC.a' | sort | tail -1)")
FX_NATIVE=$(dirname "$(find "$PKGS" -path "*$RID*" -name 'libSystem.Native.a' | sort | tail -1)")
echo "runtime libs: $ILC_NATIVE"; echo "framework libs: $FX_NATIVE"
for f in libRuntime.WorkstationGC.a libbootstrapperdll.o libeventpipe-disabled.a libstandalonegc-disabled.a libstdc++compat.a; do
  [ -f "$ILC_NATIVE/$f" ] && cp "$ILC_NATIVE/$f" "$OUT/" || echo "note: $f not found (may be fine for this .NET version)"
done
for f in libSystem.Native.a libSystem.IO.Compression.Native.a libSystem.Security.Cryptography.Native.Apple.a libSystem.Globalization.Native.a; do
  [ -f "$FX_NATIVE/$f" ] && cp "$FX_NATIVE/$f" "$OUT/" || echo "note: $f not found"
done

echo "== 3/3 Xcode project"
SYSROOT=iphoneos; [ "$RID" = iossimulator-arm64 ] && SYSROOT=iphonesimulator
cmake -S ios -B build/ios -G Xcode -DCMAKE_SYSTEM_NAME=iOS -DCMAKE_OSX_SYSROOT=$SYSROOT \
  -DCMAKE_OSX_DEPLOYMENT_TARGET=15.0 -DDEV_TEAM="${DEV_TEAM:-}"
echo "Open build/ios/StackS.xcodeproj, pick your device and team, and press Run."
