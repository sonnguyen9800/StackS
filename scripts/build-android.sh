#!/usr/bin/env bash
# Build and install the StackS Android app (arm64 devices).
# Needs: .NET 8+ SDK, Android SDK + NDK 26 (ANDROID_NDK_ROOT or ANDROID_HOME/ndk/<version>), Gradle 8.7+ or Android Studio, adb.
# NativeAOT for Android (linux-bionic) is experimental in .NET 8-10: expect to adjust flags below.
set -euo pipefail
cd "$(dirname "$0")/.."

: "${ANDROID_NDK_ROOT:=${ANDROID_HOME:-$HOME/Android/Sdk}/ndk/26.3.11579264}"
export ANDROID_NDK_ROOT

echo "== 1/3 C# game -> libstacks_game.so (NativeAOT, linux-bionic-arm64)"
dotnet publish src/StackS.Mobile -c Release -r linux-bionic-arm64 \
  -p:PublishAot=true -p:NativeLib=Shared \
  -p:PublishAotUsingRuntimePack=true -p:DisableUnsupportedError=true \
  -o build/android-nativeaot
mkdir -p android/nativeaot/arm64-v8a
cp build/android-nativeaot/stacks_game.so android/nativeaot/arm64-v8a/libstacks_game.so

echo "== 2/3 APK (Gradle builds native/ with CMake and packages both libraries)"
cd android
if [ -x ./gradlew ]; then ./gradlew assembleDebug; else gradle assembleDebug; fi

echo "== 3/3 install"
if command -v adb >/dev/null && [ "$(adb devices | grep -c 'device$')" -gt 0 ]; then
  adb install -r app/build/outputs/apk/debug/app-debug.apk
  adb shell am start -n dev.stacks.game/android.app.NativeActivity
  echo "logs: adb logcat -s StackS sokol_app"
else
  echo "APK: android/app/build/outputs/apk/debug/app-debug.apk (no device connected)"
fi
