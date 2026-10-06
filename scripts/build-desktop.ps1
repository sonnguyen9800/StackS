# Build the native desktop game on Windows (D3D11). Needs Visual Studio 2022 with C++ and CMake.
#   .\scripts\build-desktop.ps1          JIT build in build\desktop-jit
#   .\scripts\build-desktop.ps1 aot      NativeAOT executable in build\desktop
param([string]$mode = "")
$ErrorActionPreference = "Stop"
Set-Location (Join-Path $PSScriptRoot "..")
cmake -S native -B build/native
cmake --build build/native --config Release
if ($mode -eq "aot") {
  dotnet publish src/StackS.Desktop -c Release -r win-x64 -p:PublishAot=true -o build/desktop
  Write-Host "run: build\desktop\stacks-game.exe"
} else {
  dotnet build src/StackS.Desktop -c Release -o build/desktop-jit
  Write-Host "run: build\desktop-jit\stacks-game.exe"
}
