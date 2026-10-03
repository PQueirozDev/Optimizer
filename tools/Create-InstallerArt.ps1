# Gera as imagens do assistente de instalacao (Inno Setup) com a identidade visual do app:
# fundo escuro com brilho violeta/ciano, logo em quadrado com gradiente e o nome do produto.
# Saida: installer\art\wizard-large*.bmp (painel lateral) e wizard-small*.bmp (canto superior).
param([string]$OutputDirectory = (Join-Path $PSScriptRoot "..\installer\art"))

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Drawing

$logoPath = Join-Path $PSScriptRoot "..\PQueirozOptimizer\Assets\app.png"
New-Item -ItemType Directory -Force -Path $OutputDirectory | Out-Null
$logo = [System.Drawing.Image]::FromFile((Resolve-Path $logoPath))

$violet = [System.Drawing.Color]::FromArgb(139, 92, 246)
$cyan = [System.Drawing.Color]::FromArgb(34, 211, 238)
$background = [System.Drawing.Color]::FromArgb(10, 11, 18)

function New-RoundedPath([float]$x, [float]$y, [float]$w, [float]$h, [float]$r) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $d = $r * 2
    $path.AddArc($x, $y, $d, $d, 180, 90)
    $path.AddArc($x + $w - $d, $y, $d, $d, 270, 90)
    $path.AddArc($x + $w - $d, $y + $h - $d, $d, $d, 0, 90)
    $path.AddArc($x, $y + $h - $d, $d, $d, 90, 90)
    $path.CloseFigure()
    return $path
}

function Add-Glow($g, [float]$cx, [float]$cy, [float]$radius, [System.Drawing.Color]$color, [int]$alpha) {
    $path = New-Object System.Drawing.Drawing2D.GraphicsPath
    $path.AddEllipse($cx - $radius, $cy - $radius, $radius * 2, $radius * 2)
    $brush = New-Object System.Drawing.Drawing2D.PathGradientBrush($path)
    $brush.CenterColor = [System.Drawing.Color]::FromArgb($alpha, $color)
    $brush.SurroundColors = @([System.Drawing.Color]::FromArgb(0, $color))
    $g.FillPath($brush, $path)
    $brush.Dispose(); $path.Dispose()
}

function Draw-LogoTile($g, [float]$x, [float]$y, [float]$size) {
    # O logo já tem fundo escuro: ele preenche o bloco arredondado e a borda leva o gradiente da marca
    Add-Glow $g ($x + $size / 2) ($y + $size / 2) ($size * 0.95) $violet 120
    $tile = New-RoundedPath $x $y $size $size ($size * 0.26)
    $state = $g.Save()
    $g.SetClip($tile)
    $g.DrawImage($logo, $x, $y, $size, $size)
    $g.Restore($state)
    $gradient = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF($x, $y)), (New-Object System.Drawing.PointF(($x + $size), ($y + $size))), $violet, $cyan)
    $pen = New-Object System.Drawing.Pen($gradient, [Math]::Max(2, $size * 0.045))
    $g.DrawPath($pen, $tile)
    $pen.Dispose(); $gradient.Dispose(); $tile.Dispose()
}

function Save-Bmp($bitmap, [string]$name) {
    # BMP de 24 bits: o formato aceito por todas as versoes do Inno Setup
    $flat = New-Object System.Drawing.Bitmap($bitmap.Width, $bitmap.Height, [System.Drawing.Imaging.PixelFormat]::Format24bppRgb)
    $g = [System.Drawing.Graphics]::FromImage($flat)
    $g.DrawImage($bitmap, 0, 0, $bitmap.Width, $bitmap.Height)
    $g.Dispose()
    $flat.Save((Join-Path $OutputDirectory $name), [System.Drawing.Imaging.ImageFormat]::Bmp)
    $flat.Dispose()
}

function New-LargeImage([int]$scale) {
    $w = 164 * $scale; $h = 314 * $scale
    $bitmap = New-Object System.Drawing.Bitmap($w, $h)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"; $g.TextRenderingHint = "AntiAliasGridFit"
    $g.Clear($background)
    Add-Glow $g ($w * 0.85) ($h * 0.05) ($w * 0.95) $violet 150
    Add-Glow $g ($w * 0.05) ($h * 0.95) ($w * 0.9) $cyan 95
    Add-Glow $g ($w * 0.9) ($h * 0.75) ($w * 0.5) ([System.Drawing.Color]::FromArgb(236, 72, 153)) 60

    $tile = 64 * $scale
    Draw-LogoTile $g (($w - $tile) / 2) (92 * $scale) $tile

    $center = New-Object System.Drawing.StringFormat
    $center.Alignment = "Center"
    $title = New-Object System.Drawing.Font("Segoe UI", (17 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $sub = New-Object System.Drawing.Font("Segoe UI", (9 * $scale), [System.Drawing.FontStyle]::Bold, [System.Drawing.GraphicsUnit]::Pixel)
    $g.DrawString("PQueiroz", $title, [System.Drawing.Brushes]::White, (New-Object System.Drawing.RectangleF(0, (170 * $scale), $w, (26 * $scale))), $center)
    $muted = New-Object System.Drawing.SolidBrush([System.Drawing.Color]::FromArgb(160, 170, 190))
    $g.DrawString("O P T I M I Z E R", $sub, $muted, (New-Object System.Drawing.RectangleF(0, (196 * $scale), $w, (16 * $scale))), $center)

    # Linha de destaque em gradiente embaixo do nome
    $lineY = 222 * $scale
    $line = New-Object System.Drawing.Drawing2D.LinearGradientBrush((New-Object System.Drawing.PointF(($w * 0.3), 0)), (New-Object System.Drawing.PointF(($w * 0.7), 0)), $violet, $cyan)
    $g.FillRectangle($line, $w * 0.3, $lineY, $w * 0.4, 2 * $scale)

    $title.Dispose(); $sub.Dispose(); $muted.Dispose(); $line.Dispose(); $g.Dispose()
    Save-Bmp $bitmap ("wizard-large" + $(if ($scale -gt 1) { "-$($scale * 100)" } else { "" }) + ".bmp")
    $bitmap.Dispose()
}

function New-SmallImage([int]$scale) {
    $size = 55 * $scale
    $bitmap = New-Object System.Drawing.Bitmap($size, $size)
    $g = [System.Drawing.Graphics]::FromImage($bitmap)
    $g.SmoothingMode = "AntiAlias"; $g.InterpolationMode = "HighQualityBicubic"
    # O canto superior do assistente tem fundo branco
    $g.Clear([System.Drawing.Color]::White)
    $margin = 4 * $scale
    Draw-LogoTile $g $margin $margin ($size - 2 * $margin)
    $g.Dispose()
    Save-Bmp $bitmap ("wizard-small" + $(if ($scale -gt 1) { "-$($scale * 100)" } else { "" }) + ".bmp")
    $bitmap.Dispose()
}

foreach ($scale in 1, 2) { New-LargeImage $scale; New-SmallImage $scale }
$logo.Dispose()
Get-ChildItem $OutputDirectory -Filter *.bmp | ForEach-Object { "{0} ({1:N0} KB)" -f $_.Name, ($_.Length / 1KB) }
