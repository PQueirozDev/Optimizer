# Monta o video de apresentacao do site a partir das cenas reais do app.
#   1. dotnet run --project tests\Optimizer.Verification -- <pasta> --videoshots
#   2. .\tools\Make-SiteVideo.ps1 -Scenes <pasta>
# Cada cena ganha um zoom lento (camera), a legenda entra e sai com fade e as cenas se ligam por transicoes.
param(
    [Parameter(Mandatory = $true)][string]$Scenes,
    [string]$Output = (Join-Path $PSScriptRoot "..\site\assets\tour.mp4"),
    [double]$SceneSeconds = 4.6,
    [double]$TransitionSeconds = 0.7
)

$ErrorActionPreference = "Stop"
$ffmpeg = (Get-Command ffmpeg -ErrorAction SilentlyContinue).Source
if (-not $ffmpeg) { throw "ffmpeg nao encontrado. Instale com: winget install Gyan.FFmpeg" }

$images = @(Get-ChildItem -LiteralPath $Scenes -Filter "scene*.jpg" | Sort-Object Name)
if ($images.Count -lt 2) { throw "Nenhuma cena encontrada em $Scenes" }

$fps = 30
$frames = [int]($SceneSeconds * $fps)
$inv = [Globalization.CultureInfo]::InvariantCulture
$arguments = @("-y", "-hide_banner", "-loglevel", "error")
$filters = New-Object System.Collections.Generic.List[string]

for ($i = 0; $i -lt $images.Count; $i++) {
    $caption = Join-Path $Scenes ("caption{0:D2}.png" -f $i)
    $arguments += @("-i", $images[$i].FullName, "-loop", "1", "-t", $SceneSeconds.ToString($inv), "-i", $caption)
    # Zoom de 1.0 a 1.045 (cenas pares aproximam do centro, ímpares deslizam da borda esquerda até o centro). A imagem é
    # ampliada antes do zoompan para o movimento não "tremer" em passos de pixel inteiro.
    $step = (0.045 / $frames).ToString("0.000000", $inv)
    $x = if ($i % 2 -eq 0) { "iw/2-(iw/zoom/2)" } else { "(iw-iw/zoom)/2*on/$frames" }
    $filters.Add("[$(2 * $i):v]scale=3840:2160,zoompan=z='1+$step*on':x='$x':y='ih/2-(ih/zoom/2)':d=${frames}:s=1920x1080:fps=$fps,setsar=1,format=yuv420p[v$i]")
    $fadeOut = ($SceneSeconds - 1.1).ToString($inv)
    $filters.Add("[$(2 * $i + 1):v]format=rgba,fade=t=in:st=0.45:d=0.5:alpha=1,fade=t=out:st=${fadeOut}:d=0.5:alpha=1[c$i]")
    $filters.Add("[v$i][c$i]overlay=0:0:shortest=1,format=yuv420p[s$i]")
}

# Encadeia as cenas: a k-ésima transição começa em k*(cena - transição)
$transitions = @("fade", "smoothleft", "fade", "smoothup", "fade", "smoothleft", "fade", "smoothup", "fade")
$previous = "s0"
for ($k = 1; $k -lt $images.Count; $k++) {
    $offset = ($k * ($SceneSeconds - $TransitionSeconds)).ToString("0.000", $inv)
    $kind = $transitions[($k - 1) % $transitions.Count]
    $label = if ($k -eq $images.Count - 1) { "joined" } else { "x$k" }
    $filters.Add("[$previous][s$k]xfade=transition=${kind}:duration=$($TransitionSeconds.ToString($inv)):offset=$offset[$label]")
    $previous = $label
}
$total = $images.Count * $SceneSeconds - ($images.Count - 1) * $TransitionSeconds
$filters.Add("[joined]fade=t=in:st=0:d=0.6,fade=t=out:st=$(($total - 0.8).ToString('0.00', $inv)):d=0.8[out]")

$graph = Join-Path $Scenes "filtros.txt"
[IO.File]::WriteAllText($graph, ($filters -join ";`n"))
$arguments += @("-/filter_complex", $graph, "-map", "[out]", "-c:v", "libx264", "-preset", "slow", "-crf", "21", "-profile:v", "high", "-pix_fmt", "yuv420p", "-r", "$fps", "-movflags", "+faststart", "-an", $Output)

Write-Host ("==> {0} cenas, {1:0.0} s" -f $images.Count, $total)
& $ffmpeg @arguments
if ($LASTEXITCODE -ne 0) { throw "ffmpeg terminou com codigo $LASTEXITCODE" }

# Capa do vídeo (aparece antes de ele carregar)
$poster = [IO.Path]::ChangeExtension($Output, ".jpg")
& $ffmpeg -y -hide_banner -loglevel error -ss 1.6 -i $Output -frames:v 1 -q:v 3 $poster
Get-Item $Output, $poster | ForEach-Object { "{0} ({1:N1} MB)" -f $_.Name, ($_.Length / 1MB) }
