# Signs release artifacts with Authenticode when a certificate is configured.
# Skips silently when no certificate is available (local dev builds).
#
# Option A — certificate in Windows cert store (recommended for production):
#   $env:CODESIGN_CERT_THUMBPRINT = "YOUR_CERT_SHA1_THUMBPRINT"
#
# Option B — PFX file:
#   $env:CODESIGN_PFX_PATH = "C:\certs\codesign.pfx"
#   $env:CODESIGN_PFX_PASSWORD = "your-password"
#
# Requires Windows SDK signtool (ships with Visual Studio / Windows SDK).

param(
    [Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)]
    [string[]]$Files
)

function Find-SignTool {
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"
    if (-not (Test-Path $kitsRoot)) { return $null }
    $candidates = Get-ChildItem $kitsRoot -Directory -ErrorAction SilentlyContinue |
        Sort-Object Name -Descending |
        ForEach-Object { Join-Path $_.FullName "x64\signtool.exe" } |
        Where-Object { Test-Path $_ }
    return $candidates | Select-Object -First 1
}

$thumb = $env:CODESIGN_CERT_THUMBPRINT
$pfx = $env:CODESIGN_PFX_PATH
$pfxPass = $env:CODESIGN_PFX_PASSWORD

if ([string]::IsNullOrWhiteSpace($thumb) -and [string]::IsNullOrWhiteSpace($pfx)) {
    Write-Host "Code signing skipped (set CODESIGN_CERT_THUMBPRINT or CODESIGN_PFX_PATH to enable)."
    return
}

$signtool = Find-SignTool
if (-not $signtool) {
    Write-Warning "signtool.exe not found. Install the Windows SDK or Visual Studio Build Tools."
    return
}

$timestamp = "http://timestamp.digicert.com"
$signed = 0

foreach ($file in $Files) {
    if (-not (Test-Path $file)) {
        Write-Warning "Skip signing (not found): $file"
        continue
    }

    Write-Host "Signing: $file"
    if (-not [string]::IsNullOrWhiteSpace($pfx)) {
        & $signtool sign /fd SHA256 /f $pfx /p $pfxPass /tr $timestamp /td SHA256 $file
    }
    else {
        & $signtool sign /fd SHA256 /sha1 $thumb /tr $timestamp /td SHA256 $file
    }

    if ($LASTEXITCODE -ne 0) {
        throw "signtool failed for $file (exit $LASTEXITCODE)"
    }
    $signed++
}

Write-Host "Signed $signed file(s)." -ForegroundColor Green
