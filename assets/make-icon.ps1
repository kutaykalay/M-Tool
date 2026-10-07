# M-Tool icon: smoky charcoal rounded square, terracotta-orange three-blade fan (banner colours).
# Draws each size natively (GDI+, anti-aliased) and packs PNG entries into one .ico.
param([string]$OutIco, [string]$PreviewDir)
Add-Type -AssemblyName System.Drawing
$ErrorActionPreference = 'Stop'

function C([string]$hex) { [System.Drawing.ColorTranslator]::FromHtml($hex) }

function RoundedRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $p = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = 2 * $r
    $p.AddArc($x, $y, $d, $d, 180, 90)
    $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $p.CloseFigure()
    return $p
}

function Draw([int]$size) {
    $bmp = New-Object System.Drawing.Bitmap $size, $size, ([System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.PixelOffsetMode = 'HighQuality'
    $g.Clear([System.Drawing.Color]::Transparent)
    $s = $size / 256.0

    # background: charcoal "smoke" gradient like the banner, thin warm rim
    $inset = [Math]::Max(0.5, 8 * $s)
    $bg = RoundedRect $inset $inset ($size - 2 * $inset) ($size - 2 * $inset) (52 * $s)
    $bgBrush = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, 0), (New-Object System.Drawing.PointF $size, $size), (C '#2B3038'), (C '#0F1115')
    $g.FillPath($bgBrush, $bg)
    if ($size -ge 16) {
        $rim = New-Object System.Drawing.Pen ([System.Drawing.Color]::FromArgb(90, (C '#E06A3C'))), ([Math]::Max(1, 3 * $s))
        $g.DrawPath($rim, $bg)
    }

    # three blades: rotated ellipses around the centre, orange to terracotta
    $cx = $size / 2.0; $cy = $size / 2.0
    $blade = New-Object System.Drawing.Drawing2D.LinearGradientBrush (New-Object System.Drawing.PointF 0, (-100 * $s)), (New-Object System.Drawing.PointF 0, (10 * $s)), (C '#FF8A55'), (C '#C2502E')
    foreach ($angle in 0, 120, 240) {
        $state = $g.Save()
        $g.TranslateTransform($cx, $cy)
        $g.RotateTransform($angle + 18)
        $g.FillEllipse($blade, (-27 * $s), (-98 * $s), (54 * $s), (92 * $s))
        $g.Restore($state)
    }

    # hub
    $hub = 22 * $s
    $g.FillEllipse((New-Object System.Drawing.SolidBrush (C '#0F1115')), ($cx - $hub), ($cy - $hub), (2 * $hub), (2 * $hub))
    $ring = New-Object System.Drawing.Pen (C '#FF8A55'), ([Math]::Max(1, 7 * $s))
    $g.DrawEllipse($ring, ($cx - $hub), ($cy - $hub), (2 * $hub), (2 * $hub))

    $g.Dispose()
    return $bmp
}

$sizes = 16, 20, 24, 32, 40, 48, 64, 256
$pngs = foreach ($size in $sizes) {
    $bmp = Draw $size
    $ms = New-Object System.IO.MemoryStream
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    if ($PreviewDir) { $bmp.Save((Join-Path $PreviewDir "icon-$size.png"), [System.Drawing.Imaging.ImageFormat]::Png) }
    $bmp.Dispose()
    , $ms.ToArray()
}

$out = New-Object System.IO.MemoryStream
$w = New-Object System.IO.BinaryWriter $out
$w.Write([uint16]0); $w.Write([uint16]1); $w.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
    $w.Write([byte]$dim); $w.Write([byte]$dim); $w.Write([byte]0); $w.Write([byte]0)
    $w.Write([uint16]1); $w.Write([uint16]32)
    $w.Write([uint32]$pngs[$i].Length); $w.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($png in $pngs) { $w.Write($png) }
$w.Flush()
[System.IO.File]::WriteAllBytes($OutIco, $out.ToArray())
"wrote $OutIco ($($out.Length) bytes, $($sizes.Count) sizes)"
