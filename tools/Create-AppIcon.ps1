Add-Type -AssemblyName System.Drawing

$src = Join-Path $PSScriptRoot '..\PQueirozOptimizer\Assets\app.png'
$outIco = Join-Path $PSScriptRoot '..\PQueirozOptimizer\Assets\app.ico'

if (-not (Test-Path $src)) {
    Write-Error "Source PNG not found: $src"
    exit 1
}

$orig = [System.Drawing.Image]::FromFile($src)
$sizes = @(16, 24, 32, 48, 64, 128, 256)
$pngBytesList = [System.Collections.Generic.List[byte[]]]::new()

foreach ($s in $sizes) {
    $bmp = [System.Drawing.Bitmap]::new($s, $s, [System.Drawing.Imaging.PixelFormat]::Format32bppArgb)
    $g = [System.Drawing.Graphics]::FromImage($bmp)
    $g.InterpolationMode = [System.Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
    $g.SmoothingMode = [System.Drawing.Drawing2D.SmoothingMode]::HighQuality
    $g.PixelOffsetMode = [System.Drawing.Drawing2D.PixelOffsetMode]::HighQuality
    $g.DrawImage($orig, 0, 0, $s, $s)
    $g.Dispose()

    $ms = [System.IO.MemoryStream]::new()
    $bmp.Save($ms, [System.Drawing.Imaging.ImageFormat]::Png)
    $pngBytesList.Add($ms.ToArray())
    $ms.Dispose()
    $bmp.Dispose()
}
$orig.Dispose()

$fs = [System.IO.File]::Create($outIco)
$bw = [System.IO.BinaryWriter]::new($fs)

# Header
$bw.Write([int16]0)
$bw.Write([int16]1)
$bw.Write([int16]$sizes.Count)

# Directory entries
$offset = 6 + (16 * $sizes.Count)
for ($i = 0; $i -lt $sizes.Count; $i++) {
    $s = $sizes[$i]
    $data = $pngBytesList[$i]
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]$(if ($s -ge 256) { 0 } else { $s }))
    $bw.Write([byte]0)
    $bw.Write([byte]0)
    $bw.Write([int16]1)
    $bw.Write([int16]32)
    $bw.Write([int32]$data.Length)
    $bw.Write([int32]$offset)
    $offset += $data.Length
}

# Data chunks
foreach ($data in $pngBytesList) {
    $bw.Write($data)
}

$bw.Dispose()
$fs.Dispose()

Write-Host "Modern multi-resolution icon generated successfully: $outIco"
