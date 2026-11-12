# Erzeugt Knipser.ico: Fotoapparat mit Auswahlrahmen (Eckmarken) um das Objektiv.
Add-Type -AssemblyName System.Drawing

function New-Frame([int]$s) {
    $bmp = New-Object System.Drawing.Bitmap $s, $s
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.SmoothingMode = 'AntiAlias'
    $g.Clear([System.Drawing.Color]::Transparent)
    $u = $s / 100.0

    function RR($x, $y, $w, $h, $r) {
        $p = New-Object System.Drawing.Drawing2D.GraphicsPath
        $d = $r * 2
        $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
        $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
        $p.CloseFigure(); return $p
    }

    $blue  = [System.Drawing.Color]::FromArgb(255, 30, 136, 229)
    $dark  = [System.Drawing.Color]::FromArgb(255, 13, 71, 161)
    $white = [System.Drawing.Color]::White
    $orange = [System.Drawing.Color]::FromArgb(255, 255, 179, 0)

    # Sucher-Buckel oben
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), (RR (30*$u) (10*$u) (32*$u) (22*$u) (6*$u)))
    # Gehäuse
    $g.FillPath((New-Object System.Drawing.SolidBrush $blue), (RR (5*$u) (22*$u) (90*$u) (68*$u) (12*$u)))
    # Objektivfeld (dunkel) mit Auswahlrahmen
    $g.FillPath((New-Object System.Drawing.SolidBrush $dark), (RR (24*$u) (30*$u) (52*$u) (52*$u) (8*$u)))
    $cx = 50*$u; $cy = 56*$u; $r = 15*$u
    $g.FillEllipse((New-Object System.Drawing.SolidBrush ([System.Drawing.Color]::FromArgb(255, 66, 165, 245))), $cx-$r, $cy-$r, 2*$r, 2*$r)
    $r2 = 8*$u
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $white), $cx-$r2*0.9, $cy-$r2*1.1, $r2*0.9, $r2*0.9)

    # Eckmarken (Auswahlrahmen)
    $pw = [Math]::Max(1.5, 4.2*$u)
    $pen = New-Object System.Drawing.Pen $white, $pw
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    $x0 = 29*$u; $y0 = 35*$u; $x1 = 71*$u; $y1 = 77*$u; $L = 11*$u
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($x0, $y0+$L), [System.Drawing.PointF]::new($x0, $y0), [System.Drawing.PointF]::new($x0+$L, $y0)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($x1-$L, $y0), [System.Drawing.PointF]::new($x1, $y0), [System.Drawing.PointF]::new($x1, $y0+$L)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($x0, $y1-$L), [System.Drawing.PointF]::new($x0, $y1), [System.Drawing.PointF]::new($x0+$L, $y1)))
    $g.DrawLines($pen, @([System.Drawing.PointF]::new($x1-$L, $y1), [System.Drawing.PointF]::new($x1, $y1), [System.Drawing.PointF]::new($x1, $y1-$L)))

    # Blitz
    $g.FillEllipse((New-Object System.Drawing.SolidBrush $orange), 80*$u, 29*$u, 8*$u, 8*$u)
    $g.Dispose()
    return $bmp
}

$sizes = 16, 24, 32, 48, 64, 128, 256
$pngs = @()
foreach ($s in $sizes) {
    $b = New-Frame $s
    $ms = New-Object System.IO.MemoryStream
    $b.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngs += , $ms.ToArray()
    if ($s -eq 256) { $b.Save((Join-Path $PSScriptRoot 'icon-preview.png'), [System.Drawing.Imaging.ImageFormat]::Png) }
    $b.Dispose()
}

$out = Join-Path (Split-Path $PSScriptRoot) 'Knipser.ico'
$fs = [System.IO.File]::Create($out)
$bw = New-Object System.IO.BinaryWriter $fs
$bw.Write([uint16]0); $bw.Write([uint16]1); $bw.Write([uint16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]; $wh = if ($s -ge 256) { 0 } else { $s }
    $bw.Write([byte]$wh); $bw.Write([byte]$wh); $bw.Write([byte]0); $bw.Write([byte]0)
    $bw.Write([uint16]1); $bw.Write([uint16]32)
    $bw.Write([uint32]$pngs[$i].Length); $bw.Write([uint32]$offset)
    $offset += $pngs[$i].Length
}
foreach ($p in $pngs) { $bw.Write($p) }
$bw.Close(); $fs.Close()
"Icon: $out"
