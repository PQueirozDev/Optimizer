<#
.SYNOPSIS
  Desenha o ícone do Qrztweaks em vetor e gera todos os arquivos derivados.

.DESCRIPTION
  O ícone é um "Q" geométrico cuja perna é um raio, violeta sobre um bloco grafite. Cada tamanho é desenhado
  direto na resolução final (sem reduzir uma imagem grande), e os tamanhos pequenos (até 32 px) usam traço mais
  grosso e nenhum detalhe fino, para continuar nítidos na barra de tarefas. O brilho fica sempre dentro do bloco:
  fora dele o fundo é transparente de verdade.

  Saídas:
    PQueirozOptimizer\Assets\app.png      1024 px (janela, splash, ativação)
    PQueirozOptimizer\Assets\app.ico      16, 24, 32, 48, 64, 128 e 256 px
    site\favicon.ico                      mesmos tamanhos do app.ico
    site\assets\favicon-32.png            32 px
    site\assets\apple-touch-icon.png      180 px, bloco ocupando o quadro inteiro (o iOS arredonda sozinho)
    site\assets\logo.png                  256 px (o site mostra em 24-30 px)
    tools\logo\qrztweaks-icon.svg         a mesma arte em SVG, para referência e edição

  Depois rode tools\Create-InstallerArt.ps1 para atualizar as imagens do instalador.
#>
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName PresentationCore, WindowsBase

$root = Split-Path $PSScriptRoot -Parent

# ---------- Arte (grade de 512) ----------
$palette = @{
    TileTop = '#262036'; TileBottom = '#101016'; Border = '#7C3AED'
    RingLight = '#C4B5FD'; RingMid = '#8B5CF6'; RingDark = '#6D28D9'
    BoltLight = '#FFFFFF'; BoltDark = '#DDD6FE'; Glow = '#7C3AED'
}
$ringCenter = 240; $ringRadius = 124
# Raio na vertical, centrado na origem; depois é girado e levado para a perna do Q
$boltPoints = '-18,-112 58,-112 16,-14 68,-14 -40,114 -8,12 -60,12'
$boltAngle = -62; $boltX = 346; $boltY = 350; $boltScale = 0.86

function Color($hex) { [Windows.Media.ColorConverter]::ConvertFromString($hex) }
function Brush($hex, [double]$opacity = 1) { $b = [Windows.Media.SolidColorBrush]::new((Color $hex)); $b.Opacity = $opacity; $b.Freeze(); $b }
function Linear($from, $to, [double]$x1 = 0, [double]$y1 = 0, [double]$x2 = 1, [double]$y2 = 1) {
    $b = [Windows.Media.LinearGradientBrush]::new((Color $from), (Color $to), [Windows.Point]::new($x1, $y1), [Windows.Point]::new($x2, $y2)); $b.Freeze(); $b
}

function Get-BoltGeometry([double]$scale) {
    $geometry = [Windows.Media.Geometry]::Parse("M $($boltPoints.Replace(' ', ' L ')) Z").Clone()
    $transform = [Windows.Media.TransformGroup]::new()
    $transform.Children.Add([Windows.Media.ScaleTransform]::new($scale, $scale))
    $transform.Children.Add([Windows.Media.RotateTransform]::new($boltAngle))
    $transform.Children.Add([Windows.Media.TranslateTransform]::new($boltX, $boltY))
    $geometry.Transform = $transform
    $geometry
}

<#
  -Small: traço do anel mais grosso, raio maior, sem borda e sem brilho (16-32 px).
  -FullBleed: o bloco ocupa o quadro todo, sem cantos (ícone de toque da Apple).
#>
function Render-Icon([int]$size, [switch]$Small, [switch]$FullBleed) {
    $visual = [Windows.Media.DrawingVisual]::new()
    $dc = $visual.RenderOpen()
    $dc.PushTransform([Windows.Media.ScaleTransform]::new($size / 512, $size / 512))

    $tileRect = if ($FullBleed) { [Windows.Rect]::new(0, 0, 512, 512) } elseif ($Small) { [Windows.Rect]::new(8, 8, 496, 496) } else { [Windows.Rect]::new(24, 24, 464, 464) }
    $corner = if ($FullBleed) { 0 } elseif ($Small) { 104 } else { 112 }
    $tile = [Windows.Media.RectangleGeometry]::new($tileRect, $corner, $corner)
    $dc.DrawGeometry((Linear $palette.TileTop $palette.TileBottom 0 0 0 1), $null, $tile)

    $dc.PushClip($tile)
    if (-not $Small) {
        # Brilho violeta atrás do Q e luz no alto do bloco, sempre recortados pelo bloco
        $glow = [Windows.Media.RadialGradientBrush]::new()
        $glow.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.Color]::FromArgb(110, 124, 58, 237), 0))
        $glow.GradientStops.Add([Windows.Media.GradientStop]::new([Windows.Media.Color]::FromArgb(0, 124, 58, 237), 1))
        $glow.Center = [Windows.Point]::new(0.47, 0.47); $glow.GradientOrigin = $glow.Center; $glow.RadiusX = 0.5; $glow.RadiusY = 0.5
        $dc.DrawEllipse($glow, $null, [Windows.Point]::new($ringCenter, $ringCenter), 230, 230)
        $shine = [Windows.Media.LinearGradientBrush]::new([Windows.Media.Color]::FromArgb(22, 255, 255, 255), [Windows.Media.Color]::FromArgb(0, 255, 255, 255), 90)
        $dc.DrawRectangle($shine, $null, [Windows.Rect]::new(0, 0, 512, 200))
    }

    # Anel do Q com um vão em volta da perna: o vão é recortado do anel (não um contorno escuro), então só
    # aparece onde os dois se cruzam. Essencial para a perna se destacar em 16 px.
    $stroke = if ($Small) { 84 } else { 68 }
    $radius = if ($Small) { 128 } else { $ringRadius }
    $ringPen = [Windows.Media.Pen]::new([Windows.Media.Brushes]::Black, $stroke)
    $ring = [Windows.Media.EllipseGeometry]::new([Windows.Point]::new($ringCenter, $ringCenter), $radius, $radius).GetWidenedPathGeometry($ringPen)
    $bolt = Get-BoltGeometry $(if ($Small) { $boltScale * 1.18 } else { $boltScale })
    $gapPen = [Windows.Media.Pen]::new([Windows.Media.Brushes]::Black, $(if ($Small) { 44 } else { 30 }))
    $gapPen.LineJoin = [Windows.Media.PenLineJoin]::Round
    $gap = [Windows.Media.Geometry]::Combine($bolt, $bolt.GetWidenedPathGeometry($gapPen), [Windows.Media.GeometryCombineMode]::Union, $null)
    $ring = [Windows.Media.Geometry]::Combine($ring, $gap, [Windows.Media.GeometryCombineMode]::Exclude, $null)
    $dc.DrawGeometry((Linear $palette.RingLight $palette.RingDark 0.15 0 0.85 1), $null, $ring)
    $dc.DrawGeometry((Linear $palette.BoltLight $palette.BoltDark 0 0 1 1), $null, $bolt)
    $dc.Pop()

    if (-not $Small -and -not $FullBleed) {
        # Fio de borda violeta, por dentro do bloco
        $inner = [Windows.Rect]::new($tileRect.X + 2, $tileRect.Y + 2, $tileRect.Width - 4, $tileRect.Height - 4)
        $dc.DrawRoundedRectangle($null, [Windows.Media.Pen]::new((Brush $palette.Border 0.55), 4), $inner, $corner - 2, $corner - 2)
    }
    $dc.Pop()
    $dc.Close()

    $bitmap = [Windows.Media.Imaging.RenderTargetBitmap]::new($size, $size, 96, 96, [Windows.Media.PixelFormats]::Pbgra32)
    $bitmap.Render($visual)
    $bitmap.Freeze()
    $bitmap
}

function Get-PngBytes($bitmap) {
    $encoder = [Windows.Media.Imaging.PngBitmapEncoder]::new()
    $encoder.Frames.Add([Windows.Media.Imaging.BitmapFrame]::Create($bitmap))
    $stream = [IO.MemoryStream]::new()
    $encoder.Save($stream)
    $stream.ToArray()
}

function Save-Png($bitmap, $path) { [IO.File]::WriteAllBytes($path, (Get-PngBytes $bitmap)); Write-Host "  $path" }

function Save-Ico($path) {
    $sizes = 16, 24, 32, 48, 64, 128, 256
    $images = foreach ($s in $sizes) { , (Get-PngBytes (Render-Icon $s -Small:($s -le 32))) }
    $stream = [IO.File]::Create($path)
    $writer = [IO.BinaryWriter]::new($stream)
    try {
        $writer.Write([int16]0); $writer.Write([int16]1); $writer.Write([int16]$sizes.Count)
        $offset = 6 + 16 * $sizes.Count
        for ($i = 0; $i -lt $sizes.Count; $i++) {
            $dim = if ($sizes[$i] -ge 256) { 0 } else { $sizes[$i] }
            $writer.Write([byte]$dim); $writer.Write([byte]$dim); $writer.Write([byte]0); $writer.Write([byte]0)
            $writer.Write([int16]1); $writer.Write([int16]32)
            $writer.Write([int32]$images[$i].Length); $writer.Write([int32]$offset)
            $offset += $images[$i].Length
        }
        # Write(byte[], int, int): com só o array, o PowerShell escolhe Write(bool) e grava um byte por imagem
        foreach ($image in $images) { $writer.Write([byte[]]$image, 0, $image.Length) }
    } finally { $writer.Dispose(); $stream.Dispose() }
    Write-Host "  $path"
}

function Save-Svg($path) {
    $svg = @"
<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 512 512" width="512" height="512">
  <!-- Ícone do Qrztweaks: "Q" geométrico cuja perna é um raio. Gerado por tools/Create-AppIcon.ps1 (versão grande). -->
  <defs>
    <linearGradient id="tile" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="$($palette.TileTop)"/><stop offset="1" stop-color="$($palette.TileBottom)"/></linearGradient>
    <linearGradient id="ring" x1="0.15" y1="0" x2="0.85" y2="1"><stop offset="0" stop-color="$($palette.RingLight)"/><stop offset="1" stop-color="$($palette.RingDark)"/></linearGradient>
    <linearGradient id="bolt" x1="0" y1="0" x2="1" y2="1"><stop offset="0" stop-color="$($palette.BoltLight)"/><stop offset="1" stop-color="$($palette.BoltDark)"/></linearGradient>
    <radialGradient id="glow" cx="0.47" cy="0.47" r="0.5"><stop offset="0" stop-color="$($palette.Glow)" stop-opacity="0.43"/><stop offset="1" stop-color="$($palette.Glow)" stop-opacity="0"/></radialGradient>
    <linearGradient id="shine" x1="0" y1="0" x2="0" y2="1"><stop offset="0" stop-color="#fff" stop-opacity="0.13"/><stop offset="1" stop-color="#fff" stop-opacity="0"/></linearGradient>
    <mask id="gap"><rect width="512" height="512" fill="#fff"/><polygon transform="translate($boltX $boltY) rotate($boltAngle) scale($boltScale)" points="$boltPoints" fill="#000" stroke="#000" stroke-width="$([math]::Round(30 / $boltScale))" stroke-linejoin="round"/></mask>
    <clipPath id="clip"><rect x="24" y="24" width="464" height="464" rx="112"/></clipPath>
  </defs>
  <rect x="24" y="24" width="464" height="464" rx="112" fill="url(#tile)"/>
  <g clip-path="url(#clip)">
    <circle cx="$ringCenter" cy="$ringCenter" r="230" fill="url(#glow)"/>
    <rect x="0" y="0" width="512" height="200" fill="url(#shine)"/>
    <circle cx="$ringCenter" cy="$ringCenter" r="$ringRadius" fill="none" stroke="url(#ring)" stroke-width="68" mask="url(#gap)"/>
    <polygon transform="translate($boltX $boltY) rotate($boltAngle) scale($boltScale)" points="$boltPoints" fill="url(#bolt)"/>
  </g>
  <rect x="26" y="26" width="460" height="460" rx="110" fill="none" stroke="$($palette.Border)" stroke-opacity="0.55" stroke-width="4"/>
</svg>
"@
    [IO.File]::WriteAllText($path, $svg, [Text.UTF8Encoding]::new($false))
    Write-Host "  $path"
}

Write-Host 'Gerando o ícone do Qrztweaks:'
Save-Png (Render-Icon 1024) (Join-Path $root 'PQueirozOptimizer\Assets\app.png')
Save-Ico (Join-Path $root 'PQueirozOptimizer\Assets\app.ico')
Save-Ico (Join-Path $root 'site\favicon.ico')
Save-Png (Render-Icon 32 -Small) (Join-Path $root 'site\assets\favicon-32.png')
Save-Png (Render-Icon 180 -FullBleed) (Join-Path $root 'site\assets\apple-touch-icon.png')
Save-Png (Render-Icon 256) (Join-Path $root 'site\assets\logo.png')
Save-Svg (Join-Path $root 'tools\logo\qrztweaks-icon.svg')
