# Generates assets/TKVoice.ico (16–256 px, PNG-compressed entries) so the icon is reproducible from source.
param([string]$OutFile = (Join-Path $PSScriptRoot "..\assets\TKVoice.ico"))

Add-Type -AssemblyName System.Drawing

$sizes = 16, 24, 32, 48, 64, 128, 256
$images = foreach ($size in $sizes) {
    $bitmap = New-Object System.Drawing.Bitmap $size, $size
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::AntiAlias
    $g.TextRenderingHint = [System.Drawing.Text.TextRenderingHint]::AntiAliasGridFit

    $margin = [Math]::Max(1, $size / 32)
    $rect = New-Object System.Drawing.RectangleF $margin, $margin, ($size - 2 * $margin), ($size - 2 * $margin)
    $brush = New-Object System.Drawing.Drawing2D.LinearGradientBrush $rect, ([System.Drawing.Color]::FromArgb(0x1F, 0x7A, 0xE0)), ([System.Drawing.Color]::FromArgb(0x0F, 0x4C, 0xA8)), 45
    $g.FillEllipse($brush, $rect)

    $font = New-Object System.Drawing.Font "Segoe UI", ($size * 0.36), ([System.Drawing.FontStyle]::Bold), ([System.Drawing.GraphicsUnit]::Pixel)
    $format = New-Object System.Drawing.StringFormat
    $format.Alignment = [System.Drawing.StringAlignment]::Center
    $format.LineAlignment = [System.Drawing.StringAlignment]::Center
    $g.DrawString("TK", $font, [System.Drawing.Brushes]::White, $rect, $format)

    $stream = New-Object System.IO.MemoryStream
    if ($size -ge 256) {
        # Large entry as PNG (Vista+ standard).
        $bitmap.Save($stream, [System.Drawing.Imaging.ImageFormat]::Png)
    }
    else {
        # Small entries as classic 32-bit DIB: BITMAPINFOHEADER (double height), bottom-up BGRA rows, empty AND mask.
        $bw = New-Object System.IO.BinaryWriter $stream
        $bw.Write([UInt32]40); $bw.Write([Int32]$size); $bw.Write([Int32]($size * 2))
        $bw.Write([UInt16]1); $bw.Write([UInt16]32); $bw.Write([UInt32]0)
        $bw.Write([UInt32]($size * $size * 4)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
        for ($y = $size - 1; $y -ge 0; $y--) {
            for ($x = 0; $x -lt $size; $x++) {
                $c = $bitmap.GetPixel($x, $y)
                $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A)
            }
        }
        $maskRow = [int]([Math]::Ceiling($size / 32.0) * 4)
        $bw.Write((New-Object byte[] ($maskRow * $size)))
        $bw.Flush()
    }
    $g.Dispose(); $bitmap.Dispose(); $brush.Dispose(); $font.Dispose()
    , $stream.ToArray()
}

# ICO container: header, one directory entry per image, then the PNG data.
$out = New-Object System.IO.MemoryStream
$writer = New-Object System.IO.BinaryWriter $out
$writer.Write([UInt16]0); $writer.Write([UInt16]1); $writer.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dimension = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $writer.Write([byte]$dimension); $writer.Write([byte]$dimension)
    $writer.Write([byte]0); $writer.Write([byte]0)
    $writer.Write([UInt16]1); $writer.Write([UInt16]32)
    $writer.Write([UInt32]$images[$i].Length); $writer.Write([UInt32]$offset)
    $offset += $images[$i].Length
}
foreach ($image in $images) { $writer.Write($image) }

New-Item -ItemType Directory -Force (Split-Path $OutFile) | Out-Null
[System.IO.File]::WriteAllBytes($OutFile, $out.ToArray())
"Icon written: $OutFile"
