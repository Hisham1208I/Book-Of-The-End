<#
.SYNOPSIS
    Publishes the Book of the End Linux command-line edition as a single
    self-contained binary (no .NET install required on the target machine).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File build/build-linux.ps1
    # Produces dist/linux/bookoftheend

.NOTES
    Cross-compiles from Windows. The result runs on x64 Linux and must be run
    with sudo for raw block-device access:  sudo ./bookoftheend
#>
param(
    [string]$Runtime = "linux-x64",
    [string]$Configuration = "Release",
    [string]$Version = "2.4.6"
)

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$proj = Join-Path $root "src/BookOfTheEnd.Cli/BookOfTheEnd.Cli.csproj"
$outDir = Join-Path $root "dist/linux"

Write-Host "Publishing Book of the End CLI $Version for $Runtime..." -ForegroundColor Cyan

dotnet publish $proj `
    -c $Configuration `
    -r $Runtime `
    --self-contained true `
    -p:PublishSingleFile=true `
    -p:Version=$Version `
    -o $outDir

if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

Write-Host ""
Write-Host "Done. Binary at: $outDir/bookoftheend" -ForegroundColor Green
Write-Host "Copy it to your Linux machine and run:" -ForegroundColor Green
Write-Host "    chmod +x bookoftheend && sudo ./bookoftheend" -ForegroundColor Green
