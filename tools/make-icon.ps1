Add-Type -AssemblyName System.Drawing

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.Clear([System.Drawing.Color]::Transparent)

    $pad = [Math]::Max(1, [int]($size * 0.06))
    $rectSize = $size - 2 * $pad

    # rounded blue background square
    $bgRect = New-Object System.Drawing.Rectangle $pad, $pad, $rectSize, $rectSize
    $radius = [Math]::Max(2, [int]($size * 0.18))
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($bgRect.X, $bgRect.Y, $d, $d, 180, 90)
    $path.AddArc($bgRect.Right - $d, $bgRect.Y, $d, $d, 270, 90)
    $path.AddArc($bgRect.Right - $d, $bgRect.Bottom - $d, $d, $d, 0, 90)
    $path.AddArc($bgRect.X, $bgRect.Bottom - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush($bgRect, [System.Drawing.Color]::FromArgb(255,66,133,244), [System.Drawing.Color]::FromArgb(255,30,90,200), 45)
    $g.FillPath($bgBrush, $path)

    # white photo card, slightly rotated feel via inset rect
    $photoMargin = [int]($size * 0.20)
    $photoRect = New-Object System.Drawing.Rectangle ($pad + $photoMargin), ($pad + $photoMargin + [int]($size*0.04)), ($rectSize - 2*$photoMargin), ([int]($rectSize - 2*$photoMargin - $size*0.10))
    $whiteBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::White)
    $g.FillRectangle($whiteBrush, $photoRect)

    # mountain glyph inside the photo card
    $mtBrush = New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255,180,200,230))
    $mtPoints = @(
        New-Object System.Drawing.Point ([int]($photoRect.X + $photoRect.Width*0.08)), ([int]($photoRect.Bottom - $photoRect.Height*0.10))
        New-Object System.Drawing.Point ([int]($photoRect.X + $photoRect.Width*0.38)), ([int]($photoRect.Y + $photoRect.Height*0.35))
        New-Object System.Drawing.Point ([int]($photoRect.X + $photoRect.Width*0.58)), ([int]($photoRect.Y + $photoRect.Height*0.62))
        New-Object System.Drawing.Point ([int]($photoRect.X + $photoRect.Width*0.78)), ([int]($photoRect.Y + $photoRect.Height*0.30))
        New-Object System.Drawing.Point ([int]($photoRect.X + $photoRect.Width*0.94)), ([int]($photoRect.Bottom - $photoRect.Height*0.10))
    )
    $g.FillPolygon($mtBrush, $mtPoints)

    # red annotation circle overlapping bottom-right of the photo card
    $circleSize = [int]($size * 0.42)
    $circleRect = New-Object System.Drawing.Rectangle ($pad + $rectSize - [int]($circleSize*0.78)), ($pad + $rectSize - [int]($circleSize*0.78)), $circleSize, $circleSize
    $redPenWidth = [Math]::Max(2, [int]($size * 0.07))
    $redPen = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(255,235,64,52)), $redPenWidth
    $g.DrawEllipse($redPen, $circleRect)

    $g.Dispose()
    return $bmp
}

$sizes = 16,24,32,48,64,128,256
$bitmaps = $sizes | ForEach-Object { New-IconBitmap $_ }

$pngBlobs = @()
foreach ($bmp in $bitmaps) {
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBlobs += ,$ms.ToArray()
}

$outPath = "d:\personal\cc\pic\src\PicMark\App.ico"
$fs = [System.IO.File]::Open($outPath, [System.IO.FileMode]::Create)
$bw = New-Object System.IO.BinaryWriter $fs

$count = $sizes.Count
$bw.Write([UInt16]0)      # reserved
$bw.Write([UInt16]1)      # type = icon
$bw.Write([UInt16]$count)

$headerSize = 6 + 16 * $count
$offset = $headerSize
for ($i = 0; $i -lt $count; $i++) {
    $s = $sizes[$i]
    $byteSize = $pngBlobs[$i].Length
    $wByte = if ($s -ge 256) { 0 } else { $s }
    $hByte = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([Byte]$wByte)
    $bw.Write([Byte]$hByte)
    $bw.Write([Byte]0)    # color count
    $bw.Write([Byte]0)    # reserved
    $bw.Write([UInt16]1)  # planes
    $bw.Write([UInt16]32) # bit count
    $bw.Write([UInt32]$byteSize)
    $bw.Write([UInt32]$offset)
    $offset += $byteSize
}
foreach ($blob in $pngBlobs) {
    $bw.Write($blob)
}
$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Icon written to $outPath"
