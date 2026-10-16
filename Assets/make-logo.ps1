param([string]$Out)
# Renders the McpClientUI logo: a blue-to-violet rounded square with a hub connected to
# three nodes (a client talking to tools), as PNGs and a multi-size .ico.
Add-Type -AssemblyName System.Drawing
function RoundRect([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
  $p = New-Object System.Drawing.Drawing2D.GraphicsPath
  $d = $r * 2
  $p.AddArc($x, $y, $d, $d, 180, 90); $p.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
  $p.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90); $p.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
  $p.CloseFigure(); $p
}
function Render([int]$s) {
  $bmp = New-Object System.Drawing.Bitmap($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
  $g = [System.Drawing.Graphics]::FromImage($bmp)
  $g.SmoothingMode = 'AntiAlias'; $g.PixelOffsetMode = 'HighQuality'; $g.Clear([System.Drawing.Color]::Transparent)
  $m = [Math]::Max(0.5, $s * 0.03)
  $bg = RoundRect $m $m ($s - 2 * $m) ($s - 2 * $m) ($s * 0.22)
  $grad = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF($s, $s)),
    [System.Drawing.Color]::FromArgb(255, 0, 120, 212), [System.Drawing.Color]::FromArgb(255, 124, 77, 255))
  $g.FillPath($grad, $bg)
  # soft top highlight
  $hl = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(0, 0)), (New-Object System.Drawing.PointF(0, $s * 0.6)),
    [System.Drawing.Color]::FromArgb(60, 255, 255, 255), [System.Drawing.Color]::FromArgb(0, 255, 255, 255))
  $g.FillPath($hl, $bg)

  $c = $s / 2.0; $R = $s * 0.27
  $white = [System.Drawing.Color]::White
  $nodes = @(-90, 30, 150) | % { $a = $_ * [Math]::PI / 180; ,@(($c + $R * [Math]::Cos($a)), ($c + $R * [Math]::Sin($a))) }
  if ($s -ge 24) {
    $pen = New-Object System.Drawing.Pen([System.Drawing.Color]::FromArgb(230, 255, 255, 255), [float]([Math]::Max(1.5, $s * 0.055)))
    $pen.StartCap = 'Round'; $pen.EndCap = 'Round'
    foreach ($n in $nodes) { $g.DrawLine($pen, [float]$c, [float]$c, [float]$n[0], [float]$n[1]) }
  }
  $hub = $s * ($(if ($s -ge 24) { 0.21 } else { 0.30 }))
  $g.FillEllipse((New-Object System.Drawing.SolidBrush($white)), [float]($c - $hub / 2), [float]($c - $hub / 2), [float]$hub, [float]$hub)
  if ($s -ge 24) {
    # small gradient dot in the hub
    $inner = $hub * 0.42
    $g.FillEllipse((New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(255, 70, 100, 235))), [float]($c - $inner / 2), [float]($c - $inner / 2), [float]$inner, [float]$inner)
  }
  $node = $s * ($(if ($s -ge 24) { 0.14 } else { 0.2 }))
  foreach ($n in $nodes) { $g.FillEllipse((New-Object System.Drawing.SolidBrush($white)), [float]($n[0] - $node / 2), [float]($n[1] - $node / 2), [float]$node, [float]$node) }
  $g.Dispose(); $bmp
}
$sizes = 16, 20, 24, 32, 40, 48, 64, 128, 256
$pngs = @{}
foreach ($s in $sizes) {
  $bmp = Render $s; $ms = New-Object System.IO.MemoryStream
  if ($s -ge 256) { $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png) }
  else {
    # 32-bit DIB entry: BITMAPINFOHEADER (double height), bottom-up BGRA pixels, empty AND mask
    $bw = New-Object System.IO.BinaryWriter($ms)
    $mask = [int]([Math]::Ceiling($s / 32.0) * 4) * $s
    $bw.Write([UInt32]40); $bw.Write([Int32]$s); $bw.Write([Int32]($s * 2)); $bw.Write([UInt16]1); $bw.Write([UInt16]32)
    $bw.Write([UInt32]0); $bw.Write([UInt32]($s * $s * 4 + $mask)); $bw.Write([Int32]0); $bw.Write([Int32]0); $bw.Write([UInt32]0); $bw.Write([UInt32]0)
    for ($y = $s - 1; $y -ge 0; $y--) { for ($x = 0; $x -lt $s; $x++) { $c = $bmp.GetPixel($x, $y); $bw.Write([byte]$c.B); $bw.Write([byte]$c.G); $bw.Write([byte]$c.R); $bw.Write([byte]$c.A) } }
    $bw.Write((New-Object byte[] $mask)); $bw.Flush()
  }
  $pngs[$s] = $ms.ToArray()
}
(Render 512).Save((Join-Path $Out 'logo.png'), [System.Drawing.Imaging.ImageFormat]::Png)
# ICO: header + directory + PNG-compressed images
$fs = [System.IO.File]::Create((Join-Path $Out 'app.ico')); $w = New-Object System.IO.BinaryWriter($fs)
$w.Write([UInt16]0); $w.Write([UInt16]1); $w.Write([UInt16]$sizes.Count)
$offset = 6 + 16 * $sizes.Count
foreach ($s in $sizes) {
  $b = if ($s -ge 256) { 0 } else { $s }
  $w.Write([byte]$b); $w.Write([byte]$b); $w.Write([byte]0); $w.Write([byte]0)
  $w.Write([UInt16]1); $w.Write([UInt16]32); $w.Write([UInt32]$pngs[$s].Length); $w.Write([UInt32]$offset)
  $offset += $pngs[$s].Length
}
foreach ($s in $sizes) { $w.Write($pngs[$s]) }
$w.Close()
"ok"
