# Clears Windows shell icon cache after reinstalling Book of the End.
# Run from an elevated PowerShell if shortcuts still show the old logo.
$ErrorActionPreference = "Stop"

Write-Host "Stopping Explorer..."
Stop-Process -Name explorer -Force -ErrorAction SilentlyContinue

$iconCache = Join-Path $env:LOCALAPPDATA "Microsoft\Windows\Explorer"
Get-ChildItem $iconCache -Filter "iconcache*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue
Get-ChildItem $iconCache -Filter "thumbcache*" -ErrorAction SilentlyContinue | Remove-Item -Force -ErrorAction SilentlyContinue

Write-Host "Restarting Explorer..."
Start-Process explorer.exe

Write-Host "Done. Open Start and check Book of the End again."
