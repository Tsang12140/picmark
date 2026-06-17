#requires -Version 5.0
<#
Pixel-by-pixel comparison of two images. Used to verify PicMark's PNG/BMP export
is truly lossless, or to quantify how much a JPEG re-encode actually changed.

Usage:
  .\compare-images.ps1 -Original original.png -Exported exported.png
  .\compare-images.ps1 -Original original.jpg -Exported exported.jpg -DiffOut diff.png

NOTE: keep this file ASCII-only. Windows PowerShell 5.1 reads .ps1 files using the
system codepage unless a BOM is present; non-ASCII text written without a BOM gets
silently mis-decoded and can corrupt variable/token parsing, not just printed text.
#>
param(
    [Parameter(Mandatory = $true)][string]$Original,
    [Parameter(Mandatory = $true)][string]$Exported,
    [string]$DiffOut
)

Add-Type -AssemblyName System.Drawing

function Get-RawBgra([System.Drawing.Bitmap]$bmp) {
    $rect = New-Object System.Drawing.Rectangle(0, 0, $bmp.Width, $bmp.Height)
    $data = $bmp.LockBits($rect, [System.Drawing.Imaging.ImageLockMode]::ReadOnly, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $bytes = New-Object byte[] ($data.Stride * $bmp.Height)
    [System.Runtime.InteropServices.Marshal]::Copy($data.Scan0, $bytes, 0, $bytes.Length)
    $bmp.UnlockBits($data)
    return @{ Bytes = $bytes; Stride = $data.Stride }
}

if (-not (Test-Path $Original)) { throw "File not found: $Original" }
if (-not (Test-Path $Exported)) { throw "File not found: $Exported" }

$origInfo = Get-Item $Original
$expInfo = Get-Item $Exported

$bmpA = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Original))
$bmpB = [System.Drawing.Bitmap]::FromFile((Resolve-Path $Exported))

Write-Host "Original: $($origInfo.Name)  $($bmpA.Width)x$($bmpA.Height)  $([math]::Round($origInfo.Length/1KB,1)) KB"
Write-Host "Exported: $($expInfo.Name)  $($bmpB.Width)x$($bmpB.Height)  $([math]::Round($expInfo.Length/1KB,1)) KB"
$sizeDelta = $expInfo.Length - $origInfo.Length
$pct = [math]::Round(($sizeDelta / $origInfo.Length) * 100, 1)
Write-Host "File size delta: $sizeDelta bytes ($pct%)"

if ($bmpA.Width -ne $bmpB.Width -or $bmpA.Height -ne $bmpB.Height) {
    Write-Host ""
    Write-Host "Dimensions differ, cannot compare pixel-by-pixel." -ForegroundColor Yellow
    $bmpA.Dispose(); $bmpB.Dispose()
    return
}

$rawA = Get-RawBgra $bmpA
$rawB = Get-RawBgra $bmpB
$w = $bmpA.Width
$h = $bmpA.Height

$diffCount = 0
$maxChannelDiff = 0
$sumDiff = 0
$minX = $w; $minY = $h; $maxX = -1; $maxY = -1

$bytesA = $rawA.Bytes
$bytesB = $rawB.Bytes
$stride = $rawA.Stride

$diffBmp = $null
if ($DiffOut) {
    $diffBmp = New-Object System.Drawing.Bitmap($w, $h)
}

for ($y = 0; $y -lt $h; $y++) {
    $rowOffset = $y * $stride
    for ($x = 0; $x -lt $w; $x++) {
        $i = $rowOffset + $x * 4
        $db = [math]::Abs([int]$bytesA[$i]     - [int]$bytesB[$i])
        $dg = [math]::Abs([int]$bytesA[$i + 1] - [int]$bytesB[$i + 1])
        $dr = [math]::Abs([int]$bytesA[$i + 2] - [int]$bytesB[$i + 2])
        $da = [math]::Abs([int]$bytesA[$i + 3] - [int]$bytesB[$i + 3])
        $localMax = [math]::Max([math]::Max($db, $dg), [math]::Max($dr, $da))

        if ($localMax -gt 0) {
            $diffCount++
            $sumDiff += $localMax
            if ($localMax -gt $maxChannelDiff) { $maxChannelDiff = $localMax }
            if ($x -lt $minX) { $minX = $x }
            if ($x -gt $maxX) { $maxX = $x }
            if ($y -lt $minY) { $minY = $y }
            if ($y -gt $maxY) { $maxY = $y }
            if ($diffBmp) { $diffBmp.SetPixel($x, $y, [System.Drawing.Color]::Red) }
        }
        elseif ($diffBmp) {
            $diffBmp.SetPixel($x, $y, [System.Drawing.Color]::FromArgb(40, 40, 40))
        }
    }
}

$totalPixels = $w * $h
$diffPct = [math]::Round(($diffCount / $totalPixels) * 100, 4)

Write-Host ""
if ($diffCount -eq 0) {
    Write-Host "Result: identical, 0 differing pixels -- truly lossless." -ForegroundColor Green
}
else {
    Write-Host "Result: $diffCount / $totalPixels pixels differ ($diffPct%)" -ForegroundColor Yellow
    Write-Host "Max single-channel delta: $maxChannelDiff / 255"
    Write-Host "Average delta (over differing pixels only): $([math]::Round($sumDiff / $diffCount, 2))"
    Write-Host "Bounding box of differences: ($minX,$minY) - ($maxX,$maxY), size $($maxX-$minX+1)x$($maxY-$minY+1)"
}

if ($diffBmp) {
    $diffBmp.Save($DiffOut, [System.Drawing.Imaging.ImageFormat]::Png)
    $diffBmp.Dispose()
    Write-Host ""
    Write-Host "Diff visualization saved to: $DiffOut (red = differing pixel)"
}

$bmpA.Dispose()
$bmpB.Dispose()
