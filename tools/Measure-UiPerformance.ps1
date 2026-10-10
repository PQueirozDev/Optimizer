# Mede o desempenho da interface do Qrztweaks sem interacao (modo de medicao do proprio app):
#  - abertura: do inicio do processo ate a Visao geral utilizavel (varias execucoes, mediana);
#  - app parado na Visao geral: CPU media, quadros por segundo, memoria;
#  - troca de paginas: tempo de cada uma, mediana e maximo;
#  - app parado em Configuracoes (sem o painel ao vivo).
# O app abre sem pedir administrador (RunAsInvoker) e fecha sozinho ao terminar.
param(
    [string]$Exe = (Join-Path $PSScriptRoot '..\PQueirozOptimizer\bin\Release\net8.0-windows\Qrztweaks.exe'),
    [int]$Runs = 5,
    [int]$IdleSeconds = 30,
    [string]$Label = 'atual',
    [string]$Output
)
$ErrorActionPreference = 'Stop'
$Exe = (Resolve-Path $Exe).Path

function Invoke-Profile([int]$idle, [bool]$nav) {
    $file = Join-Path $env:TEMP ("qrz-profile-" + [guid]::NewGuid().ToString('N') + '.txt')
    $env:PQO_STARTUP_PROFILE = $file
    $env:PQO_PROFILE_IDLE = "$idle"
    $env:PQO_PROFILE_NAV = $(if ($nav) { '1' } else { '0' })
    $env:__COMPAT_LAYER = 'RunAsInvoker'
    try {
        $p = Start-Process -FilePath $Exe -PassThru
        $limit = 90000 + 1000 * ($idle * 2) + $(if ($nav) { 60000 } else { 0 })
        if (-not $p.WaitForExit($limit)) { $p.Kill(); throw "O app nao terminou a medicao em $([math]::Round($limit / 1000)) s." }
    }
    finally {
        Remove-Item Env:PQO_STARTUP_PROFILE, Env:PQO_PROFILE_IDLE, Env:PQO_PROFILE_NAV, Env:__COMPAT_LAYER -ErrorAction SilentlyContinue
    }
    if (-not (Test-Path $file)) { throw 'O app fechou sem gravar as medicoes.' }
    $values = @{}
    foreach ($line in Get-Content $file) {
        $parts = $line -split "`t"
        if ($parts.Count -eq 2) { $values[$parts[0]] = [double]::Parse($parts[1], [Globalization.CultureInfo]::InvariantCulture) }
    }
    Remove-Item $file -Force
    return $values
}

function Median([double[]]$items) {
    $sorted = $items | Sort-Object
    if ($sorted.Count -eq 0) { return $null }
    return $sorted[[math]::Floor($sorted.Count / 2)]
}

$startups = @()
for ($i = 1; $i -le $Runs; $i++) {
    $r = Invoke-Profile 0 $false
    $startups += $r['usable']
    Write-Host ("Abertura {0}: {1:N0} ms" -f $i, $r['usable'])
}
$full = Invoke-Profile $IdleSeconds $true

$warm = if ($startups.Count -gt 1) { $startups[1..($startups.Count - 1)] } else { $startups }
$navPages = $full.Keys | Where-Object { $_ -like 'nav-*-ms' -and $_ -notin 'nav-median-ms', 'nav-max-ms' } | Sort-Object
$lines = @(
    "## Medicao: $Label ($(Get-Date -Format 'yyyy-MM-dd HH:mm'))",
    '',
    '| Medida | Valor |',
    '|---|---|',
    ('| Abertura (1a execucao) | {0:N0} ms |' -f $startups[0]),
    ('| Abertura (mediana das seguintes) | {0:N0} ms |' -f (Median $warm)),
    ('| CPU parado na Visao geral (monitor ao vivo) | {0:N2} % |' -f $full['idle-dashboard-cpu-percent']),
    ('| Quadros/s parado na Visao geral | {0:N1} |' -f $full['idle-dashboard-frames-per-second']),
    ('| Memoria (working set) na Visao geral | {0:N0} MB |' -f $full['idle-dashboard-working-set-mb']),
    ('| Memoria privada na Visao geral | {0:N0} MB |' -f $full['idle-dashboard-private-mb']),
    ('| Heap gerenciado na Visao geral | {0:N0} MB |' -f $full['idle-dashboard-managed-mb']),
    ('| Troca de pagina (mediana) | {0:N0} ms |' -f $full['nav-median-ms']),
    ('| Troca de pagina (pior) | {0:N0} ms |' -f $full['nav-max-ms']),
    ('| CPU parado em Configuracoes | {0:N2} % |' -f $full['idle-settings-cpu-percent']),
    ('| Quadros/s parado em Configuracoes | {0:N1} |' -f $full['idle-settings-frames-per-second']),
    ('| Memoria (working set) depois de abrir todas as paginas | {0:N0} MB |' -f $full['idle-settings-working-set-mb']),
    '',
    'Troca de cada pagina (ms): ' + (($navPages | ForEach-Object { '{0} {1:N0}' -f ($_ -replace '^nav-|-ms$', ''), $full[$_] }) -join ', ')
)
$lines | ForEach-Object { Write-Host $_ }
if ($Output) { Add-Content -Path $Output -Value ($lines + '') -Encoding UTF8 }
