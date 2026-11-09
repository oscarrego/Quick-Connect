# ============================================================
# Generates app.ico (Bluetooth symbol, 16/32/48/256 px)
# Uses System.Drawing — no external tools needed
# ============================================================
Add-Type -AssemblyName System.Drawing

function New-BluetoothBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g   = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode     = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic

    # Blue circle background
    $pad  = [int]([Math]::Max(1, $size * 0.04))
    $diam = $size - $pad * 2
    $bgBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 25, 118, 210))
    $g.FillEllipse($bgBrush, $pad, $pad, $diam, $diam)
    $bgBrush.Dispose()

    # Bluetooth symbol points (as doubles — no f suffix in PowerShell)
    $f   = [double]$size
    $top = [System.Drawing.PointF]::new([float]($f * 0.50), [float]($f * 0.14))
    $bot = [System.Drawing.PointF]::new([float]($f * 0.50), [float]($f * 0.86))
    $mid = [System.Drawing.PointF]::new([float]($f * 0.50), [float]($f * 0.50))
    $ur  = [System.Drawing.PointF]::new([float]($f * 0.72), [float]($f * 0.28))
    $lr  = [System.Drawing.PointF]::new([float]($f * 0.72), [float]($f * 0.72))
    $ul  = [System.Drawing.PointF]::new([float]($f * 0.28), [float]($f * 0.28))
    $ll  = [System.Drawing.PointF]::new([float]($f * 0.28), [float]($f * 0.72))

    $strokeW = [float]([Math]::Max(1.5, $size / 10.0))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::White, $strokeW)
    $pen.StartCap = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.EndCap   = [System.Drawing.Drawing2D.LineCap]::Round
    $pen.LineJoin = [System.Drawing.Drawing2D.LineJoin]::Round

    # Vertical bar
    $g.DrawLine($pen, $top, $bot)
    # top -> upper-right -> mid
    $g.DrawLine($pen, $top, $ur)
    $g.DrawLine($pen, $ur,  $mid)
    # mid -> lower-right -> bot
    $g.DrawLine($pen, $mid, $lr)
    $g.DrawLine($pen, $lr,  $bot)
    # left wings
    $g.DrawLine($pen, $top, $ul)
    $g.DrawLine($pen, $bot, $ll)

    $pen.Dispose()
    $g.Dispose()
    return $bmp
}

function Save-Ico([string]$path) {
    $sizes    = @(16, 32, 48, 256)
    $pngBytes = @{}

    foreach ($s in $sizes) {
        $bmp = New-BluetoothBitmap $s
        $ms  = New-Object System.IO.MemoryStream
        $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp.Dispose()
        $pngBytes[$s] = $ms.ToArray()
        $ms.Dispose()
    }

    # Build ICO:  6-byte header  +  16 bytes/entry  +  PNG blobs
    $out = New-Object System.IO.MemoryStream
    $w   = New-Object System.IO.BinaryWriter($out)

    $w.Write([uint16]0)                 # reserved
    $w.Write([uint16]1)                 # ICO
    $w.Write([uint16]$sizes.Count)

    $dataOffset = [uint32](6 + $sizes.Count * 16)
    foreach ($s in $sizes) {
        $wh   = if ($s -eq 256) { [byte]0 } else { [byte]$s }
        $data = $pngBytes[$s]
        $w.Write($wh)                   # width
        $w.Write($wh)                   # height
        $w.Write([byte]0)               # color count
        $w.Write([byte]0)               # reserved
        $w.Write([uint16]1)             # planes
        $w.Write([uint16]32)            # bpp
        $w.Write([uint32]$data.Length)
        $w.Write($dataOffset)
        $dataOffset += [uint32]$data.Length
    }
    foreach ($s in $sizes) { $w.Write($pngBytes[$s]) }

    $w.Flush()
    [System.IO.File]::WriteAllBytes($path, $out.ToArray())
    $w.Dispose()
    $out.Dispose()

    Write-Host ("Icon saved -> {0}  ({1} KB)" -f $path, [Math]::Round((Get-Item $path).Length / 1KB, 1))
}

Save-Ico "c:\Users\oscar\Desktop\Quick Connect\app.ico"
