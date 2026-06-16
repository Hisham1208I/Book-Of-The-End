# Builds a self-contained publish output and packages it into signed MSI + setup.exe.
# Requires: .NET 8 SDK, WiX Toolset 5+ (winget install WiXToolset.WiX)
#
# Optional code signing (removes SmartScreen "Unknown publisher" warnings):
#   $env:CODESIGN_CERT_THUMBPRINT = "YOUR_SHA1_THUMBPRINT"
# or
#   $env:CODESIGN_PFX_PATH = "C:\path\to\codesign.pfx"
#   $env:CODESIGN_PFX_PASSWORD = "password"
param(
    [ValidateSet("Release", "Debug")]
    [string]$Configuration = "Release",
    [string]$Version = "2.4.3",
    [switch]$SkipSign,
    [switch]$SkipWebView2Download
)

$ErrorActionPreference = "Stop"
$Root = Resolve-Path (Join-Path $PSScriptRoot "..")
$PublishDir = Join-Path $Root "publish"
$DistDir = Join-Path $Root "dist"
$RedistDir = Join-Path $Root "installer\redist"
$MsiName = "BookOfTheEnd-$Version-x64.msi"
$SetupName = "BookOfTheEnd-$Version-x64-Setup.exe"
$WebView2Url = "https://go.microsoft.com/fwlink/p/?LinkId=2124703"
$WebView2Setup = Join-Path $RedistDir "MicrosoftEdgeWebview2Setup.exe"

function Invoke-Sign([string[]]$Files) {
    if ($SkipSign) { return }
    & (Join-Path $PSScriptRoot "sign-artifacts.ps1") @Files
}

Write-Host "==> Generating logo assets..."
& (Join-Path $PSScriptRoot "generate-logo.ps1")

Write-Host "==> Publishing Book of the End ($Configuration, win-x64, self-contained)..."
Push-Location $Root
try {
    # Clean Release output so ApplicationIcon is re-embedded after logo changes.
    dotnet clean src/BookOfTheEnd/BookOfTheEnd.csproj -c $Configuration -nologo | Out-Null
    if (Test-Path $PublishDir) {
        Remove-Item $PublishDir -Recurse -Force
    }
    dotnet publish src/BookOfTheEnd/BookOfTheEnd.csproj `
        -c $Configuration `
        -r win-x64 `
        --self-contained true `
        -p:PublishSingleFile=false `
        -o $PublishDir
    if ($LASTEXITCODE -ne 0) { throw "dotnet publish failed with exit code $LASTEXITCODE" }

    # Sign the main executable before it is harvested into the MSI.
    $mainExe = Join-Path $PublishDir "BookOfTheEnd.exe"
    Invoke-Sign @($mainExe)

    if (-not (Get-Command dotnet -ErrorAction SilentlyContinue)) {
        throw ".NET SDK not found. Install with: winget install Microsoft.DotNet.SDK.8"
    }

    if (-not $SkipWebView2Download) {
        New-Item -ItemType Directory -Force -Path $RedistDir | Out-Null
        if (-not (Test-Path $WebView2Setup)) {
            Write-Host "==> Downloading WebView2 Evergreen bootstrapper..."
            Invoke-WebRequest -Uri $WebView2Url -OutFile $WebView2Setup -UseBasicParsing
        }
        else {
            Write-Host "==> WebView2 bootstrapper already present."
        }
    }
    elseif (-not (Test-Path $WebView2Setup)) {
        throw "WebView2 bootstrapper missing at $WebView2Setup (run without -SkipWebView2Download)"
    }

    New-Item -ItemType Directory -Force -Path $DistDir | Out-Null
    $MsiPath = Join-Path $DistDir $MsiName
    $SetupPath = Join-Path $DistDir $SetupName

    Write-Host "==> Building MSI..."
    dotnet build (Join-Path $Root "installer\BookOfTheEnd.Installer.wixproj") `
        -c Release -nologo -p:SkipPublishApp=true
    if ($LASTEXITCODE -ne 0) { throw "MSI build failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path $MsiPath)) { throw "MSI not found at $MsiPath" }

    Invoke-Sign @($MsiPath)

    Write-Host "==> Building setup.exe (WebView2 + MSI bundle)..."
    dotnet build (Join-Path $Root "installer\BookOfTheEnd.Bundle.wixproj") -c Release -nologo
    if ($LASTEXITCODE -ne 0) { throw "bundle build failed with exit code $LASTEXITCODE" }
    if (-not (Test-Path $SetupPath)) { throw "Setup.exe not found at $SetupPath" }

    Invoke-Sign @($SetupPath)

    $msiMb = [math]::Round((Get-Item $MsiPath).Length / 1MB, 1)
    $setupMb = [math]::Round((Get-Item $SetupPath).Length / 1MB, 1)
    Write-Host ""
    Write-Host "Installers ready:" -ForegroundColor Green
    Write-Host ("  Recommended: {0}  ({1} MB) - includes WebView2 if needed" -f $SetupPath, $setupMb)
    Write-Host ("  MSI only:    {0}  ({1} MB)" -f $MsiPath, $msiMb)
    Write-Host ""
    Write-Host "Share BookOfTheEnd-$Version-x64-Setup.exe with end users."
    Write-Host "Install: double-click Setup.exe, or run:"
    Write-Host ('  "' + $SetupPath + '"')
}
finally {
    Pop-Location
}
