#!/usr/bin/env bash
# Build the native desktop game (Linux/macOS). Windows: scripts/build-desktop.ps1
#   scripts/build-desktop.sh          JIT build, runs from build/desktop-jit
#   scripts/build-desktop.sh aot      NativeAOT single executable in build/desktop
set -euo pipefail
cd "$(dirname "$0")/.."
cmake -S native -B build/native -DCMAKE_BUILD_TYPE=Release
cmake --build build/native --config Release
if [ "${1:-}" = "aot" ]; then
  case "$(uname -s)-$(uname -m)" in
    Darwin-arm64) RID=osx-arm64;; Darwin-x86_64) RID=osx-x64;; Linux-aarch64) RID=linux-arm64;; *) RID=linux-x64;;
  esac
  dotnet publish src/StackS.Desktop -c Release -r "$RID" -p:PublishAot=true -o build/desktop
  echo "run: build/desktop/stacks-game"
else
  dotnet build src/StackS.Desktop -c Release -o build/desktop-jit
  echo "run: build/desktop-jit/stacks-game"
fi
