# generate-icon.ps1 — Generates Assets/app.ico for Markdown Pro
Add-Type -AssemblyName System.Drawing

$outDir = Join-Path $PSScriptRoot "MarkdownPro\MarkdownPro\Assets"
if (-not (Test-Path $outDir)) {
    New-Item -ItemType Directory -Path $outDir -Force | Out-Null
}
$icoPath = Join-Path $outDir "app.ico"

function New-IconBitmap([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap($size, $size, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.Clear([System.Drawing.Color]::Transparent)

    $margin = [float]([Math]::Max(1, [Math]::Round($size * 0.06)))
    $rectSize = [float]($size - ($margin * 2))
    $radius = [float]([Math]::Max(2, [Math]::Round($size * 0.22)))

    # Rounded rectangle path
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $radius * 2
    $path.AddArc($margin, $margin, $d, $d, 180, 90)
    $path.AddArc($margin + $rectSize - $d, $margin, $d, $d, 270, 90)
    $path.AddArc($margin + $rectSize - $d, $margin + $rectSize - $d, $d, $d, 0, 90)
    $path.AddArc($margin, $margin + $rectSize - $d, $d, $d, 90, 90)
    $path.CloseFigure()

    # Gradient background (#0F172A -> #1E3A8A -> #2563EB)
    $rect = New-Object System.Drawing.RectangleF($margin, $margin, $rectSize, $rectSize)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush(
        $rect,
        [System.Drawing.Color]::FromArgb(255, 15, 23, 42),
        [System.Drawing.Color]::FromArgb(255, 37, 99, 235),
        [System.Drawing.Drawing2D.LinearGradientMode]::ForwardDiagonal
    )
    $g.FillPath($brush, $path)

    # Border
    $penWidth = [float]([Math]::Max(1.0, $size * 0.03))
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(140, 96, 165, 250), $penWidth)
    $g.DrawPath($pen, $path)

    # Draw "M↓" or "MD" badge
    $fontSize = [float]($size * 0.36)
    $font = New-Object System.Drawing.Font("Segoe UI", $fontSize, [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sf = New-Object System.Drawing.StringFormat
    $sf.Alignment = [System.Drawing.StringAlignment]::Center
    $sf.LineAlignment = [System.Drawing.StringAlignment]::Center

    $textBrush = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 255, 255, 255))
    $centerRect = New-Object System.Drawing.RectangleF(0, 0, [float]$size, [float]$size)
    $g.DrawString("MD", $font, $textBrush, $centerRect, $sf)

    $textBrush.Dispose()
    $sf.Dispose()
    $font.Dispose()
    $pen.Dispose()
    $brush.Dispose()
    $path.Dispose()
    $g.Dispose()

    return $bmp
}

$sizes = @(16, 32, 48, 64, 128, 256)
$pngStreams = @()

foreach ($s in $sizes) {
    $bmp = New-IconBitmap $s
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngStreams += ,([byte[]]$ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}

$fs = [System.IO.File]::Create($icoPath)
$bw = New-Object System.IO.BinaryWriter($fs)

# ICONDIR header
$bw.Write([uint16]0) # Reserved
$bw.Write([uint16]1) # Type: 1 = ICO
$bw.Write([uint16]$sizes.Count)

$offset = 6 + (16 * $sizes.Count)

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngStreams[$i]
    $w = if ($s -ge 256) { [byte]0 } else { [byte]$s }
    $h = if ($s -ge 256) { [byte]0 } else { [byte]$s }

    $bw.Write($w)
    $bw.Write($h)
    $bw.Write([byte]0)   # Color count
    $bw.Write([byte]0)   # Reserved
    $bw.Write([uint16]1) # Color planes
    $bw.Write([uint16]32)# Bits per pixel
    $bw.Write([uint32]$data.Length)
    $bw.Write([uint32]$offset)

    $offset += $data.Length
}

for ($i = 0; $i -lt $sizes.Count; $i++) {
    $bw.Write($pngStreams[$i])
}

$bw.Flush()
$bw.Close()
$fs.Close()

Write-Host "Generated multi-size icon: $icoPath"

