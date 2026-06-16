# Generates a minimal modern app icon (PNG + ICO) for Book of the End.
# Also writes installer/bootstrapper logo assets used by Setup.exe.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$Root = Split-Path $PSScriptRoot -Parent
$OutPng256 = Join-Path $Root "src\BookOfTheEnd\Assets\logo-256.png"
$OutIco = Join-Path $Root "src\BookOfTheEnd\app.ico"
$InstallerAssets = Join-Path $Root "installer\assets"
$OutPng64 = Join-Path $InstallerAssets "logo-64.png"

New-Item -ItemType Directory -Force -Path (Split-Path $OutPng256) | Out-Null
New-Item -ItemType Directory -Force -Path $InstallerAssets | Out-Null

function New-LogoBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)

    $accent = [System.Drawing.Color]::FromArgb(255, 124, 58, 237)
    $accentSoft = [System.Drawing.Color]::FromArgb(210, 124, 58, 237)
    $leftBrush = New-Object System.Drawing.SolidBrush $accentSoft
    $rightBrush = New-Object System.Drawing.SolidBrush $accent
    $spineBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(120, 124, 58, 237))

    function Scale([double]$x, [double]$y) {
        [System.Drawing.PointF]::new([single]($x * $size / 24), [single]($y * $size / 24))
    }

    $left = @((Scale 4 5), (Scale 12 8.5), (Scale 12 19), (Scale 4 15.5))
    $g.FillPolygon($leftBrush, $left)
    $right = @((Scale 20 5), (Scale 12 8.5), (Scale 12 19), (Scale 20 15.5))
    $g.FillPolygon($rightBrush, $right)
    $spine = New-Object System.Drawing.RectangleF (
        [single](11.25 * $size / 24), [single](8 * $size / 24),
        [single](1.5 * $size / 24), [single](11 * $size / 24))
    $g.FillRectangle($spineBrush, $spine)

    $g.Dispose()
    return $bmp
}

$bmp256 = New-LogoBitmap 256
$bmp256.Save($OutPng256, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp256.Dispose()
Write-Host "Wrote $OutPng256"

$bmp64 = New-LogoBitmap 64
$bmp64.Save($OutPng64, [System.Drawing.Imaging.ImageFormat]::Png)
$bmp64.Dispose()
Write-Host "Wrote $OutPng64"

Push-Location (Split-Path $OutIco)
try {
    & (Join-Path $PSScriptRoot "png-to-ico.ps1") -Source $OutPng256 -Output "app.ico"
    Move-Item -Force (Join-Path (Get-Location) "app.ico") $OutIco
    Write-Host "Wrote $OutIco"
}
finally {
    Pop-Location
}
