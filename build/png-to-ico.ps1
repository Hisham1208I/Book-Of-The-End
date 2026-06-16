param(
    [Parameter(Mandatory = $true)][string]$Source,
    [Parameter(Mandatory = $true)][string]$Output
)

Add-Type -AssemblyName System.Drawing

# Shell-friendly sizes (BGRA bitmap entries, not PNG-compressed).
$sizes = @(16, 20, 24, 32, 40, 48, 64, 128, 256)
$srcPath = (Resolve-Path $Source).Path
$src = [System.Drawing.Bitmap]::FromFile($srcPath)

function New-ScaledBitmap([System.Drawing.Bitmap]$source, [int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.CompositingQuality = [System.Drawing.Drawing2D.CompositingQuality]::HighQuality
    $g.Clear([System.Drawing.Color]::Transparent)
    $g.DrawImage($source, 0, 0, $size, $size)
    $g.Dispose()
    return $bmp
}

function Get-IconImageBytes([System.Drawing.Bitmap]$bmp) {
    $w = $bmp.Width
    $h = $bmp.Height

    $rect = New-Object System.Drawing.Rectangle 0, 0, $w, $h
    $data = $bmp.LockBits(
        $rect,
        [System.Drawing.Imaging.ImageLockMode]::ReadOnly,
        [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)

    $stride = $data.Stride
    $pixels = New-Object byte[] ($stride * $h)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $pixels, 0, $pixels.Length)
    $bmp.UnlockBits($data)

    $header = New-Object byte[] 40
    [BitConverter]::GetBytes([UInt32]40).CopyTo($header, 0)          # biSize
    [BitConverter]::GetBytes([Int32]$w).CopyTo($header, 4)          # biWidth
    [BitConverter]::GetBytes([Int32]($h * 2)).CopyTo($header, 8)    # biHeight (XOR + AND)
    [BitConverter]::GetBytes([UInt16]1).CopyTo($header, 12)         # biPlanes
    [BitConverter]::GetBytes([UInt16]32).CopyTo($header, 14)        # biBitCount
    [BitConverter]::GetBytes([UInt32]0).CopyTo($header, 16)         # biCompression = BI_RGB

    $xorRowBytes = $w * 4
    $xor = New-Object byte[] ($xorRowBytes * $h)
    for ($y = 0; $y -lt $h; $y++) {
        $srcY = $h - 1 - $y
        for ($x = 0; $x -lt $w; $x++) {
            $srcIdx = ($srcY * $stride) + ($x * 4)
            $dstIdx = ($y * $xorRowBytes) + ($x * 4)
            $xor[$dstIdx] = $pixels[$srcIdx]         # B
            $xor[$dstIdx + 1] = $pixels[$srcIdx + 1] # G
            $xor[$dstIdx + 2] = $pixels[$srcIdx + 2] # R
            $xor[$dstIdx + 3] = $pixels[$srcIdx + 3] # A
        }
    }

    # 32-bit icons carry alpha in the XOR bitmap; AND mask is all zeros.
    $andRowBytes = [int][Math]::Ceiling($w / 32.0) * 4
    $and = New-Object byte[] ($andRowBytes * $h)

    $image = New-Object byte[] ($header.Length + $xor.Length + $and.Length)
    [Array]::Copy($header, 0, $image, 0, $header.Length)
    [Array]::Copy($xor, 0, $image, $header.Length, $xor.Length)
    [Array]::Copy($and, 0, $image, $header.Length + $xor.Length, $and.Length)
    return $image
}

$images = New-Object 'System.Collections.Generic.List[object]'
foreach ($s in $sizes) {
    $scaled = New-ScaledBitmap $src $s
    $imageBytes = Get-IconImageBytes $scaled
    if ($imageBytes.Length -lt 40) {
        throw "Icon image data for size $s is too small ($($imageBytes.Length) bytes)."
    }
    [void]$images.Add([ordered]@{ Size = $s; Bytes = $imageBytes })
    $scaled.Dispose()
}
$src.Dispose()

$out = New-Object System.IO.MemoryStream
$bw = New-Object System.IO.BinaryWriter($out)

$bw.Write([UInt16]0)
$bw.Write([UInt16]1)
$bw.Write([UInt16]$images.Count)

$offset = 6 + (16 * $images.Count)
foreach ($img in $images) {
    $dim = $img.Size
    if ($dim -ge 256) { $dim = 0 }
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]$dim)
    $bw.Write([Byte]0)
    $bw.Write([Byte]0)
    $bw.Write([UInt16]1)
    $bw.Write([UInt16]32)
    $bw.Write([UInt32]$img.Bytes.Length)
    $bw.Write([UInt32]$offset)
    $offset += $img.Bytes.Length
}

foreach ($img in $images) {
    $bytes = [byte[]]$img.Bytes
    $bw.Write($bytes, 0, $bytes.Length)
}

$streamLen = $out.Length
$totalBytes = 0
foreach ($img in $images) { $totalBytes += $img.Bytes.Length }
if ($streamLen -lt 1000) {
    throw "ICO stream too small ($streamLen bytes, payload $totalBytes bytes)."
}

$outPath = Join-Path (Get-Location) $Output
$bw.Flush()
[System.IO.File]::WriteAllBytes($outPath, $out.ToArray())
$bw.Dispose()
$out.Dispose()

Write-Host "Wrote $outPath ($($images.Count) BGRA sizes)"
