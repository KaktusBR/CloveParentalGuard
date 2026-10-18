# Builds the single, self-contained CloveParentalGuard.exe that a parent double-clicks.
# Output: <repo>\publish\CloveParentalGuard.exe  (no .NET install required on the target PC)
#
# Usage:  pwsh install/Build-CloveParentalGuard.ps1        (or run from any PowerShell)
$ErrorActionPreference = "Stop"
$root = Split-Path $PSScriptRoot -Parent
$out  = Join-Path $root "publish"

if (Test-Path $out) { Remove-Item $out -Recurse -Force }

dotnet publish "$root/src/CloveParentalGuard/CloveParentalGuard.csproj" `
    -c Release -r win-x64 --self-contained `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none -p:DebugSymbols=false `
    -o $out

$exe = Join-Path $out "CloveParentalGuard.exe"
Write-Host ""
Write-Host "Built: $exe"
Write-Host "Hand this one file to the parent. Double-click it to install (one UAC prompt)."
