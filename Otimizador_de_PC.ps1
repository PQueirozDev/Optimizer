<#
=====================================================================
 OTIMIZADOR DE PC - Padrao / Gamer / Debloat / Reverter / Verificar / Inicializacao
 (c) Pedro Queiroz - Todos os direitos reservados
=====================================================================
 Este script:
   - Limpa arquivos temporarios, cache e lixeira
   - Otimiza servicos e configuracoes do Windows
   - No modo Gamer, aplica ajustes extras de desempenho/latencia
   - No modo Debloat, remove apps/servicos desnecessarios (pergunta
     antes de cada etapa, ou aplica tudo de uma vez no modo rapido)
   - Permite reverter automaticamente a ultima otimizacao realizada
   - Mostra um diagnostico da saude do sistema (CPU, RAM, disco, etc.)
   - Permite gerenciar quais programas abrem junto com o Windows

 IMPORTANTE:
   - Precisa ser executado como Administrador (o script se
     autoeleva automaticamente).
   - Cria um Ponto de Restauracao antes de qualquer alteracao,
     para que voce possa desfazer se algo nao ficar do seu agrado.
=====================================================================
#>

param(
    [ValidateSet("", "analisar", "inteligente", "padrao", "gamer", "gamerservicos", "debloat", "reverter", "sfc", "dism", "chkdsk", "update", "benchmark", "reparar", "corrupcao")]
    [string]$Operation = "",
    [switch]$UiMode,
    [string]$SelectedStepsBase64 = ""
)

# ---------------------------------------------------------------
# 0. Auto-elevacao (garante que o script rode como Administrador)
# ---------------------------------------------------------------
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
    if ($UiMode) { [Console]::Error.WriteLine("Abra o aplicativo como administrador."); exit 1 }
    Write-Host "Reiniciando como Administrador..." -ForegroundColor Yellow
    Try {
        Start-Process powershell.exe "-NoProfile -ExecutionPolicy Bypass -File `"$PSCommandPath`"" -Verb RunAs -ErrorAction Stop
    } Catch {
        Write-Host ""
        Write-Host "Nao foi possivel obter permissao de Administrador (a janela do UAC foi cancelada ou bloqueada)." -ForegroundColor Red
        Read-Host "Pressione ENTER para sair"
    }
    exit
}

$ErrorActionPreference = "Continue"
if ($UiMode) {
    [Console]::OutputEncoding = New-Object System.Text.UTF8Encoding($false)
    $OutputEncoding = [Console]::OutputEncoding
    $ProgressPreference = 'SilentlyContinue'
}
$script:SelectedSteps = $null
if ($SelectedStepsBase64) {
    $script:SelectedSteps = @([Text.Encoding]::UTF8.GetString([Convert]::FromBase64String($SelectedStepsBase64)) | ConvertFrom-Json)
}

# ---------------------------------------------------------------
# ESTADO GLOBAL - usado pelo sistema de Reverter Ultima Otimizacao
# ---------------------------------------------------------------
$script:DadosDir     = "$env:ProgramData\OtimizadorPC"
$script:SnapshotDir  = Join-Path $script:DadosDir "Snapshots"
$script:SnapshotPath = Join-Path $script:SnapshotDir "ultima_otimizacao.json"
$script:LimiteHistorico = 20

# Somente estes itens podem ser capturados e restaurados. O Reverter roda como
# administrador, entao nunca confia cegamente no conteudo do arquivo de backup.
$script:RegistroPermitido = @(
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent",
    "HKLM:\SOFTWARE\Policies\Microsoft\Speech",
    "HKCU:\Software\Policies\Microsoft\Windows\Explorer",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI",
    "HKLM:\SOFTWARE\Policies\Microsoft\PushToInstall",
    "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl",
    "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile",
    "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile\Tasks\Games",
    "HKCU:\System\GameConfigStore",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR",
    "HKCU:\Software\Microsoft\GameBar",
    "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects",
    "HKCU:\Control Panel\Mouse",
    "HKCU:\Control Panel\Desktop",
    "HKCU:\Control Panel\Desktop\WindowMetrics",
    "HKCU:\Software\Microsoft\Windows\DWM",
    "HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization",
    "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences",
    "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo",
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Search",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\System",
    "HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot",
    "HKCU:\Software\Policies\Microsoft\Windows\WindowsAI",
    "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppPrivacy",
    "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power"
)
$script:TarefasPermitidas = @(
    "\Microsoft\Windows\Application Experience\Microsoft Compatibility Appraiser",
    "\Microsoft\Windows\Application Experience\ProgramDataUpdater",
    "\Microsoft\Windows\Application Experience\StartupAppTask",
    "\Microsoft\Windows\Autochk\Proxy",
    "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
    "\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
    "\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
    "\Microsoft\Windows\DiskDiagnostic\Microsoft-Windows-DiskDiagnosticDataCollector",
    "\Microsoft\Windows\Feedback\Siuf\DmClient",
    "\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
)
# Chaves que dependem do hardware (ID do dispositivo PCI) e por isso nao cabem numa lista fixa.
# Cada padrao so libera os valores listados para ele.
$script:RegistroPermitidoPadroes = @(
    @{ Padrao = '^HKLM:\\SYSTEM\\CurrentControlSet\\Enum\\PCI\\VEN_[0-9A-Fa-f]{4}&DEV_[0-9A-Fa-f]{4}[^\\]*\\[^\\]+\\Device Parameters\\Interrupt Management\\MessageSignaledInterruptProperties$'; Nomes = @("MSISupported") },
    # Tarefas do agendador multimidia (Audio, Capture, Games...): so os valores que o Reparar-MMCSS recria
    @{ Padrao = '^HKLM:\\SOFTWARE\\Microsoft\\Windows NT\\CurrentVersion\\Multimedia\\SystemProfile\\Tasks\\[^\\]+$'; Nomes = @("Affinity", "Clock Rate", "GPU Priority", "SFIO Priority", "Priority", "Scheduling Category", "Background Only", "BackgroundPriority", "Latency Sensitive") }
    # Adaptadores de video (classe Display): so o ULPS das placas AMD
    @{ Padrao = '^HKLM:\\SYSTEM\\CurrentControlSet\\Control\\Class\\\{4d36e968-e325-11ce-bfc1-08002be10318\}\\\d{4}$'; Nomes = @("EnableUlps") }
)
$script:ServicosPermitidos = @(
    "PushToInstall", "SysMain", "WSearch", "DiagTrack", "dmwappushservice",
    "diagnosticshub.standardcollector.service", "WerSvc", "PcaSvc", "Spooler", "bthserv",
    "Fax", "RemoteRegistry", "MapsBroker", "WdiServiceHost", "WdiSystemHost", "DPS"
)
$script:TiposRegistroPermitidos = @("String", "ExpandString", "Binary", "DWord", "MultiString", "QWord")
$script:StartupTypesPermitidos = @("Automatic", "AutomaticDelayedStart", "Manual", "Disabled")
$script:InstaladoresOneDrive = @(
    "$env:SystemRoot\System32\OneDriveSetup.exe",
    "$env:SystemRoot\SysWOW64\OneDriveSetup.exe"
)
$script:RunKeysPermitidas = @(
    "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\Software\Microsoft\Windows\CurrentVersion\Run",
    "HKLM:\Software\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"
)
$script:SnapshotAtual = @()
$script:SnapshotNome  = ""
# Ajuste em execucao: cada item do backup guarda de qual ajuste veio, para poder ser desfeito sozinho
$script:EtapaAtual    = $null
$script:ContAplicados = 0
$script:ContPulados   = 0
$script:ContFalhas    = 0

# ---------------------------------------------------------------
# APARENCIA DO CONSOLE
# ---------------------------------------------------------------
function Set-Aparencia {
    $Host.UI.RawUI.WindowTitle = "Otimizador de PC"
    Try {
        $Host.UI.RawUI.BackgroundColor = "Black"
        $Host.UI.RawUI.ForegroundColor = "Gray"
        $bufferSize = $Host.UI.RawUI.BufferSize
        $bufferSize.Width = 94
        $Host.UI.RawUI.BufferSize = $bufferSize
        $windowSize = $Host.UI.RawUI.WindowSize
        $windowSize.Width = 94
        $windowSize.Height = 42
        $Host.UI.RawUI.WindowSize = $windowSize
    } Catch { }
    Clear-Host
}

$Largura = 70

function Linha($char = "-") {
    Write-Host ("" + ($char.ToString() * $Largura)) -ForegroundColor DarkCyan
}

function Centralizar($texto) {
    $espacos = [Math]::Max(0, [Math]::Floor(($Largura - $texto.Length) / 2))
    return (" " * $espacos) + $texto
}

$script:HeaderLarg = 84

function Header-Topo { Write-Host ("  " + "╔" + ("═" * $script:HeaderLarg) + "╗") -ForegroundColor Cyan }
function Header-Base { Write-Host ("  " + "╚" + ("═" * $script:HeaderLarg) + "╝") -ForegroundColor Cyan }

# Linha do cabecalho com texto alinhado a esquerda e a direita na mesma linha
function Header-Linha($esq, $dir, $corEsq = "White", $corDir = "DarkGray") {
    $meio = $script:HeaderLarg - $esq.Length - $dir.Length
    if ($meio -lt 1) { $meio = 1 }
    Write-Host "  ║" -NoNewline -ForegroundColor Cyan
    Write-Host $esq -NoNewline -ForegroundColor $corEsq
    Write-Host (" " * $meio) -NoNewline
    Write-Host $dir -NoNewline -ForegroundColor $corDir
    Write-Host "║" -ForegroundColor Cyan
}

function Write-Banner {
    Clear-Host
    Write-Host ""
    Header-Topo
    Header-Linha "  >_ OTIMIZADOR DE PC" "v1.2  " "Cyan" "DarkGray"
    Header-Linha "     Analise, otimizacao inteligente e manutencao do Windows" "by Pedro Queiroz  " "DarkGray" "DarkGray"
    Header-Base
    Write-Host ""
}

function Write-Secao($texto) {
    Write-Host ""
    Linha "="
    Write-Host (Centralizar $texto.ToUpper()) -ForegroundColor Yellow
    Linha "="
    Write-Host ""
}

function Write-Resultado($ok, $texto) {
    if ($ok) {
        Write-Host "   [" -NoNewline -ForegroundColor DarkGray
        Write-Host "OK" -NoNewline -ForegroundColor Green
        Write-Host "] " -NoNewline -ForegroundColor DarkGray
        Write-Host $texto -ForegroundColor Gray
    } else {
        Write-Host "   [" -NoNewline -ForegroundColor DarkGray
        Write-Host "!!" -NoNewline -ForegroundColor Red
        Write-Host "] " -NoNewline -ForegroundColor DarkGray
        Write-Host "$texto (falhou ou nao se aplica neste PC)" -ForegroundColor DarkYellow
    }
}

# Linha usada quando o usuario opta por PULAR uma etapa do Debloat
function Write-Pulado($texto) {
    Write-Host "   [" -NoNewline -ForegroundColor DarkGray
    Write-Host "--" -NoNewline -ForegroundColor DarkGray
    Write-Host "] " -NoNewline -ForegroundColor DarkGray
    Write-Host "Pulado: $texto" -ForegroundColor DarkGray
}

# Pergunta Sim/Nao ao usuario. Retorna $true para S/Sim, $false para qualquer outra coisa (inclusive ENTER vazio)
function Confirmar($pergunta) {
    Write-Host ""
    if ($UiMode) { return $false }
    $resp = Read-Host "  >> $pergunta (S/N)"
    return ($resp -match '^[Ss]')
}

# Bolinha colorida para linhas de diagnostico (ok / warn / bad / info)
function Write-Status($nivel, $texto) {
    if ($UiMode) {
        # O app colore a linha pela marcacao. "bad" e um resultado da analise, nao uma falha da operacao.
        $marca = switch ($nivel) { "ok" { "[OK]" } "warn" { "[AVISO]" } "bad" { "[AVISO]" } default { "[INFO]" } }
        Write-Host "$marca $texto"
        return
    }
    switch ($nivel) {
        "ok"   { $cor = "Green" }
        "warn" { $cor = "Yellow" }
        "bad"  { $cor = "Red" }
        default { $cor = "White" }
    }
    Write-Host "   • " -NoNewline -ForegroundColor DarkGray
    Write-Host $texto -ForegroundColor $cor
}

function Format-Bytes($bytes) {
    if ($bytes -ge 1GB) { return "{0:N1} GB" -f ($bytes / 1GB) }
    elseif ($bytes -ge 1MB) { return "{0:N0} MB" -f ($bytes / 1MB) }
    else { return "{0:N0} KB" -f ($bytes / 1KB) }
}

# ---------------------------------------------------------------
# SISTEMA DE SNAPSHOT / REVERSAO
# ---------------------------------------------------------------

# Garante que somente Administradores/SYSTEM possam escrever nos backups.
# Por padrao, usuarios comuns podem criar arquivos em subpastas de ProgramData;
# sem isso, um usuario sem privilegios poderia plantar um backup falso que o
# Reverter (executado como administrador) aplicaria.
function Proteger-PastaDados {
    $confiaveis = @("S-1-5-32-544", "S-1-5-18")
    foreach ($pasta in @($script:DadosDir, $script:SnapshotDir, (Join-Path $script:DadosDir "Backups"))) {
        $item = Get-Item -LiteralPath $pasta -Force -ErrorAction SilentlyContinue
        if ($item -and ($item.Attributes -band [IO.FileAttributes]::ReparsePoint)) {
            # Junction/symlink plantado: remove apenas o link, sem seguir o destino
            [IO.Directory]::Delete($pasta)
        }
        New-Item -Path $pasta -ItemType Directory -Force | Out-Null

        $admins = New-Object Security.Principal.SecurityIdentifier "S-1-5-32-544"
        $system = New-Object Security.Principal.SecurityIdentifier "S-1-5-18"
        $acl = New-Object Security.AccessControl.DirectorySecurity
        $acl.SetAccessRuleProtection($true, $false)
        $acl.SetOwner($admins)
        foreach ($sid in @($admins, $system)) {
            $acl.AddAccessRule((New-Object Security.AccessControl.FileSystemAccessRule($sid, "FullControl", "ContainerInherit,ObjectInherit", "None", "Allow")))
        }
        Set-Acl -LiteralPath $pasta -AclObject $acl -ErrorAction Stop

        # Arquivos criados por outra conta nao sao confiaveis: ficam em quarentena
        # (renomeados) e nunca sao lidos. Com a ACL acima, quem os criou nao consegue
        # mais renomea-los de volta.
        Get-ChildItem -LiteralPath $pasta -Force -File -ErrorAction SilentlyContinue |
            Where-Object { $_.Extension -ne ".nao-confiavel" } | ForEach-Object {
                $donoArquivo = (Get-Acl -LiteralPath $_.FullName).GetOwner([Security.Principal.SecurityIdentifier]).Value
                if (($_.Attributes -band [IO.FileAttributes]::ReparsePoint) -or $donoArquivo -notin $confiaveis) {
                    $destino = "$($_.FullName).{0}.nao-confiavel" -f [Guid]::NewGuid().ToString('N')
                    [IO.File]::Move($_.FullName, $destino)
                    Write-Host "[AVISO] Arquivo de backup de origem desconhecida isolado: $($_.Name)"
                }
            }
    }
}

# Mantem somente os arquivos mais recentes de historico/reversao
function Limpar-HistoricoAntigo {
    Get-ChildItem -LiteralPath $script:SnapshotDir -File -ErrorAction SilentlyContinue |
        Where-Object { $_.Name -like "historico_*.json" -or $_.Name -like "revertido_*.json" } |
        Sort-Object LastWriteTime -Descending |
        Select-Object -Skip $script:LimiteHistorico |
        Remove-Item -Force -ErrorAction SilentlyContinue
}

# Comeca a registrar uma nova sessao de otimizacao.
# Se ainda existe um backup nao revertido, ele e mantido e as novas capturas sao
# acrescentadas: assim o Reverter sempre volta ao estado ORIGINAL, mesmo que o
# usuario rode varias otimizacoes seguidas (a primeira captura de cada item vence).
function Iniciar-Snapshot($nome) {
    $script:SnapshotAtual = @()
    $script:SnapshotNome  = $nome
    if (Test-Path -LiteralPath $script:SnapshotPath) {
        $historyPath = Join-Path $script:SnapshotDir ("historico_{0}.json" -f [Guid]::NewGuid().ToString('N'))
        Copy-Item -LiteralPath $script:SnapshotPath -Destination $historyPath -ErrorAction Stop
        Try {
            $anterior = Get-Content -LiteralPath $script:SnapshotPath -Raw | ConvertFrom-Json
            $script:SnapshotAtual = @($anterior.Itens | Where-Object { $_ })
            if ($anterior.Nome -and $anterior.Nome -ne $nome) { $script:SnapshotNome = "$($anterior.Nome) + $nome" }
        } Catch {
            Write-Host "[AVISO] Backup anterior ilegivel; arquivado em $historyPath"
            Remove-Item -LiteralPath $script:SnapshotPath -Force
        }
    }
    Limpar-HistoricoAntigo
    $script:ContAplicados = 0
    $script:ContPulados   = 0
    $script:ContFalhas    = 0
    Salvar-Snapshot
}

# Retorna $true se o item ja foi capturado nesta sessao ou em uma sessao ainda nao revertida
function Ja-Capturado($tipo, $chave) {
    foreach ($item in $script:SnapshotAtual) {
        if ($item.Tipo -ne $tipo) { continue }
        switch ($tipo) {
            "Registro"       { if ("$($item.Caminho)|$($item.Nome)" -eq $chave) { return $true } }
            "PlanoEnergia"   { return $true }
            default          { if ($item.Nome -eq $chave -or $item.Caminho -eq $chave) { return $true } }
        }
    }
    return $false
}

function Test-RegistroPermitido($caminho, $nome) {
    if ($script:RegistroPermitido -contains $caminho) { return $true }
    foreach ($regra in $script:RegistroPermitidoPadroes) {
        if ("$caminho" -match $regra.Padrao -and $regra.Nomes -contains $nome) { return $true }
    }
    return $false
}

function Test-ItemPermitido($item) {
    switch ($item.Tipo) {
        "Registro" {
            return (Test-RegistroPermitido $item.Caminho $item.Nome) -and
                   ($item.Nome -is [string]) -and
                   (-not $item.Existia -or -not $item.TipoAnterior -or $script:TiposRegistroPermitidos -contains $item.TipoAnterior)
        }
        "Servico" {
            return ($script:ServicosPermitidos -contains $item.Nome) -and ($script:StartupTypesPermitidos -contains $item.StartupTypeAnterior)
        }
        "PlanoEnergia"   { $g = [guid]::Empty; return [guid]::TryParse("$($item.GuidAnterior)", [ref]$g) -and $g -ne [guid]::Empty }
        "TarefaAgendada" { return $script:TarefasPermitidas -contains "$($item.Nome)" }
        "OneDrive"       { return $script:InstaladoresOneDrive -contains $item.Caminho }
        "Hibernacao"     { return $true }
        "Irreversivel"   { return $true }
        default          { return $false }
    }
}

# Salva em disco tudo que foi registrado nesta sessao (sobrescreve o snapshot anterior:
# so guardamos a ULTIMA otimizacao, que e o que o Reverter usa)
function Salvar-Snapshot {
    if ($script:SnapshotAtual.Count -eq 0) { return }
    Try {
        New-Item -Path $script:SnapshotDir -ItemType Directory -Force | Out-Null
        $objeto = [PSCustomObject]@{
            Nome  = $script:SnapshotNome
            Data  = (Get-Date).ToString("dd/MM/yyyy HH:mm:ss")
            Itens = $script:SnapshotAtual
        }
        $tempPath = $script:SnapshotPath + '.tmp'
        $objeto | ConvertTo-Json -Depth 6 | Out-File -LiteralPath $tempPath -Encoding UTF8 -Force -ErrorAction Stop
        Move-Item -LiteralPath $tempPath -Destination $script:SnapshotPath -Force -ErrorAction Stop
    } Catch { throw "Nao foi possivel salvar o backup: $($_.Exception.Message)" }
}

# Guarda o valor ATUAL de uma chave/valor de registro antes de ele ser alterado
function Capturar-Registro($caminho, $nome) {
    if (-not (Test-RegistroPermitido $caminho $nome)) { throw "Chave de registro fora da lista permitida: $caminho" }
    if (Ja-Capturado "Registro" "$caminho|$nome") { return }
    $existia = $false
    $valorAnterior = $null
    $tipoAnterior = $null
    if (Test-Path $caminho) {
        $key = Get-Item -LiteralPath $caminho -ErrorAction Stop
        if ($key.GetValueNames() -contains $nome) {
            $existia = $true
            $valorAnterior = $key.GetValue($nome, $null, [Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)
            $tipoAnterior = $key.GetValueKind($nome).ToString()
        }
    }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo          = "Registro"
        Etapa         = $script:EtapaAtual
        Caminho       = $caminho
        Nome          = $nome
        Existia       = $existia
        ValorAnterior = $valorAnterior
        TipoAnterior = $tipoAnterior
    }
    Salvar-Snapshot
}

# Guarda o tipo de inicializacao e o status ATUAL de um servico antes de ele ser alterado
function Capturar-Servico($nomeServico) {
    if ($script:ServicosPermitidos -notcontains $nomeServico) { throw "Servico fora da lista permitida: $nomeServico" }
    if (Ja-Capturado "Servico" $nomeServico) { return }
    Try {
        $svc = Get-Service -Name $nomeServico -ErrorAction Stop
        $tipo = $svc.StartType.ToString()
        # O PowerShell 5.1 nao distingue "Automatico (atraso na inicializacao)", padrao do Windows Search;
        # sem isso a reversao devolveria o servico como Automatico comum.
        if ($tipo -eq "Automatic" -and (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Services\$nomeServico" -Name DelayedAutostart -ErrorAction SilentlyContinue).DelayedAutostart -eq 1) {
            $tipo = "AutomaticDelayedStart"
        }
        $script:SnapshotAtual += [PSCustomObject]@{
            Tipo                = "Servico"
            Etapa               = $script:EtapaAtual
            Nome                = $nomeServico
            StartupTypeAnterior = $tipo
            StatusAnterior      = $svc.Status.ToString()
        }
    } Catch { throw }
    Salvar-Snapshot
}

# Guarda qual e o plano de energia ATIVO antes de trocar de plano
function Capturar-PlanoEnergia {
    if (Ja-Capturado "PlanoEnergia" "") { return }
    $atual = powercfg /getactivescheme
    if ($atual -match "([0-9a-fA-F-]{36})") {
        $script:SnapshotAtual += [PSCustomObject]@{
            Tipo         = "PlanoEnergia"
            Etapa        = $script:EtapaAtual
            GuidAnterior = $matches[1]
        }
    } else { throw 'Nao foi possivel capturar o plano de energia atual.' }
    Salvar-Snapshot
}

# Guarda se uma tarefa agendada estava ativa (para poder reativar depois).
# Retorna $false quando a tarefa nao existe: varias foram removidas em versoes recentes do Windows 11.
function Capturar-TarefaAgendada($nomeTarefa) {
    if ($script:TarefasPermitidas -notcontains $nomeTarefa) { throw "Tarefa fora da lista permitida: $nomeTarefa" }
    $corte = $nomeTarefa.LastIndexOf('\') + 1
    $tarefa = Get-ScheduledTask -TaskPath $nomeTarefa.Substring(0, $corte) -TaskName $nomeTarefa.Substring($corte) -ErrorAction SilentlyContinue
    if (-not $tarefa) { return $false }
    if (Ja-Capturado "TarefaAgendada" $nomeTarefa) { return $true }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo = "TarefaAgendada"
        Etapa = $script:EtapaAtual
        Nome = $nomeTarefa
        HabilitadaAnterior = "$($tarefa.State)" -ne "Disabled"
    }
    Salvar-Snapshot
    return $true
}

# Guarda o caminho do instalador do OneDrive (para poder reinstalar depois)
function Capturar-OneDrive($caminhoInstalador) {
    if ($script:InstaladoresOneDrive -notcontains $caminhoInstalador) { throw "Instalador do OneDrive inesperado: $caminhoInstalador" }
    if (Ja-Capturado "OneDrive" $caminhoInstalador) { return }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo    = "OneDrive"
        Etapa   = $script:EtapaAtual
        Caminho = $caminhoInstalador
    }
    Salvar-Snapshot
}

# Guarda que a hibernacao estava ligada: a reversao a religa com "powercfg /h on", que recria o hiberfil.sys
function Capturar-Hibernacao {
    if (Ja-Capturado "Hibernacao" "Hibernacao") { return }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo  = "Hibernacao"
        Etapa = $script:EtapaAtual
        Nome  = "Hibernacao"
    }
    Salvar-Snapshot
}

# Registra uma alteracao que NAO pode ser desfeita automaticamente (ex: apps removidos)
function Registrar-Irreversivel($descricao) {
    if (@($script:SnapshotAtual | Where-Object { $_.Tipo -eq "Irreversivel" -and $_.Descricao -eq $descricao }).Count -gt 0) { return }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo      = "Irreversivel"
        Etapa     = $script:EtapaAtual
        Descricao = $descricao
    }
    Salvar-Snapshot
}

# Executa uma lista de etapas mostrando barra de progresso + contador + status OK/FALHOU
function Executar-Etapas($atividade, $etapas) {
    $total = $etapas.Count
    $i = 0
    if ($UiMode) {
        # Plano para a tela de progresso do app: so as etapas que de fato vao rodar
        $plano = @($etapas | Where-Object { $atividade -eq 'Ponto de restauracao' -or $null -eq $script:SelectedSteps -or $_.Nome -in $script:SelectedSteps } | ForEach-Object { [string]$_.Nome })
        Write-Host "[PLANO] $(ConvertTo-Json -InputObject @{ Atividade = $atividade; Etapas = $plano } -Compress)"
    }
    foreach ($etapa in $etapas) {
        $i++
        if ($UiMode -and $atividade -ne 'Ponto de restauracao' -and $null -ne $script:SelectedSteps -and $etapa.Nome -notin $script:SelectedSteps) {
            $script:ContPulados++; Write-Host "[IGNORADA] $($etapa.Nome)"; continue
        }
        $script:EtapaAtual = $etapa.Nome
        if ($UiMode) { Write-Host "[ETAPA] $($etapa.Nome)" }
        $percent = [Math]::Round(($i / $total) * 100)
        Write-Progress -Activity $atividade -Status "[$i/$total] $($etapa.Nome)" -PercentComplete $percent
        $sucesso = $true
        Try {
            $ErrorActionPreference = 'Stop'
            $global:LASTEXITCODE = 0
            & $etapa.Acao | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Comando retornou codigo $LASTEXITCODE" }
        } Catch {
            $sucesso = $false
            Write-Host "[FALHA] $($etapa.Nome): $($_.Exception.Message)"
            if ($atividade -eq 'Ponto de restauracao') { throw }
        }
        if ($sucesso) { $script:ContAplicados++ } else { $script:ContFalhas++ }
        Write-Host "  [$i/$total] " -NoNewline -ForegroundColor DarkCyan
        Write-Resultado $sucesso $etapa.Nome
        Start-Sleep -Milliseconds 120
    }
    Write-Progress -Activity $atividade -Completed
}

# Pergunta S/N (ou pula a pergunta se $modoRapido) e, se confirmado, executa a etapa.
function Executar-Se-Confirmado($pergunta, $nomeEtapa, $acao, $modoRapido = $false) {
    $aplicar = $modoRapido
    if ($UiMode) { $aplicar = $null -ne $script:SelectedSteps -and $nomeEtapa -in $script:SelectedSteps }
    elseif (-not $modoRapido) {
        $aplicar = Confirmar $pergunta
    }
    if ($aplicar) {
        $script:EtapaAtual = $nomeEtapa
        if ($UiMode) { Write-Host "[ETAPA] $nomeEtapa" }
        $sucesso = $true
        Try {
            $ErrorActionPreference = 'Stop'
            $global:LASTEXITCODE = 0
            & $acao | Out-Null
            if ($LASTEXITCODE -ne 0) { throw "Comando retornou codigo $LASTEXITCODE" }
        } Catch {
            $sucesso = $false
            Write-Host "[FALHA] ${nomeEtapa}: $($_.Exception.Message)"
        }
        if ($sucesso) { $script:ContAplicados++ } else { $script:ContFalhas++ }
        Write-Resultado $sucesso $nomeEtapa
    } else {
        $script:ContPulados++
        Write-Pulado $nomeEtapa
    }
}

# ---------------------------------------------------------------
# 1. Ponto de restauracao (seguranca antes de mexer no sistema)
# ---------------------------------------------------------------
# O Windows so cria um ponto de restauracao a cada 24h por padrao; nesse caso o
# Checkpoint-Computer apenas emite um aviso e nada e criado. Liberamos o limite
# durante a criacao e devolvemos o valor original em seguida.
function Criar-PontoRestauracaoSemLimite {
    $chave = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\SystemRestore"
    $nome  = "SystemRestorePointCreationFrequency"
    $original = (Get-ItemProperty -LiteralPath $chave -Name $nome -ErrorAction SilentlyContinue).$nome
    Try {
        Set-ItemProperty -LiteralPath $chave -Name $nome -Value 0 -Type DWord -ErrorAction Stop
        Checkpoint-Computer -Description "Antes do Qrztweaks" -RestorePointType "MODIFY_SETTINGS" -ErrorAction Stop -WarningAction Stop
    } Finally {
        if ($null -eq $original) { Remove-ItemProperty -LiteralPath $chave -Name $nome -ErrorAction SilentlyContinue }
        else { Set-ItemProperty -LiteralPath $chave -Name $nome -Value $original -Type DWord -ErrorAction SilentlyContinue }
    }
}

function Criar-PontoDeRestauracao {
    Write-Secao "Criando ponto de restauracao"
    $etapas = @(
        @{ Nome = "Habilitando restauracao do sistema no disco"; Acao = { Enable-ComputerRestore -Drive "$env:SystemDrive\" } }
        @{ Nome = "Criando ponto de restauracao"; Acao = { Criar-PontoRestauracaoSemLimite } }
    )
    Executar-Etapas "Ponto de restauracao" $etapas
}

# ---------------------------------------------------------------
# 2. Limpeza de arquivos temporarios (usada nas duas versoes)
# ---------------------------------------------------------------
# $env:TEMP vem do ambiente de quem abriu o script: so e apagado se for mesmo uma pasta Temp
# (nunca a raiz de uma unidade, um link ou outra pasta qualquer, ja que o script roda como administrador)
function Obter-PastaTempSegura {
    $pasta = [IO.Path]::GetFullPath("$env:TEMP").TrimEnd('\')
    $item = Get-Item -LiteralPath $pasta -Force -ErrorAction Stop
    if ($item.Attributes -band [IO.FileAttributes]::ReparsePoint) { throw "A pasta TEMP ($pasta) e um link; limpeza cancelada." }
    if ($pasta -eq [IO.Path]::GetPathRoot($pasta).TrimEnd('\') -or (Split-Path $pasta -Leaf) -notmatch '^(Temp|Tmp)$') {
        throw "A pasta TEMP ($pasta) nao parece uma pasta temporaria; limpeza cancelada."
    }
    return $pasta
}

function Limpar-Temporarios {
    if ($UiMode) { Write-Host 'Limpeza de arquivos: use a analise e selecao na Limpeza Rapida.'; return }
    Write-Secao "Limpando arquivos temporarios e cache"
    $etapas = @(
        @{ Nome = "Pasta TEMP do usuario";              Acao = { Remove-Item "$(Obter-PastaTempSegura)\*" -Recurse -Force } }
        @{ Nome = "Pasta TEMP do Windows";               Acao = { Remove-Item "$env:SystemRoot\Temp\*" -Recurse -Force } }
        @{ Nome = "Lixeira";                              Acao = { Clear-RecycleBin -Force } }
        @{ Nome = "Cache de miniaturas";                 Acao = { Remove-Item "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db" -Force } }
        @{ Nome = "Cache do Chrome";                     Acao = { Remove-Item "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cache" -Recurse -Force } }
        @{ Nome = "Cache do Edge";                       Acao = { Remove-Item "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cache" -Recurse -Force } }
        @{ Nome = "Cache do Firefox";                    Acao = { Remove-Item "$env:APPDATA\Mozilla\Firefox\Profiles\*\cache2" -Recurse -Force } }
    )
    Executar-Etapas "Limpeza de temporarios" $etapas
}

# O "cleanmgr /sagerun:N" so limpa as categorias marcadas antes para o perfil N; sem essa marcacao
# ele termina sem apagar nada. Usa um perfil proprio (o /sageset:1 do usuario fica intacto) e so
# categorias seguras: nada de Lixeira, Downloads, Windows.old, arquivos de reset, dumps de erro,
# drivers antigos ou cache de shaders (apagar esse cache causa travadas nos jogos).
$script:PerfilCleanmgr = 4242
$script:CategoriasCleanmgr = @(
    "Active Setup Temp Folders", "BranchCache", "Delivery Optimization Files", "Diagnostic Data Viewer database files",
    "Downloaded Program Files", "Feedback Hub Archive log files", "Internet Cache Files", "Old ChkDsk Files",
    "Setup Log Files", "Temporary Files", "Temporary Setup Files", "Thumbnail Cache",
    "Windows Error Reporting Files", "Windows Upgrade Log Files"
)

function Executar-Cleanmgr {
    $base = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Explorer\VolumeCaches"
    $marca = "StateFlags{0:D4}" -f $script:PerfilCleanmgr
    foreach ($categoria in $script:CategoriasCleanmgr) {
        $chave = Join-Path $base $categoria
        if (Test-Path -LiteralPath $chave) { Set-ItemProperty -LiteralPath $chave -Name $marca -Value 2 -Type DWord }
    }
    $processo = Start-Process cleanmgr.exe -ArgumentList "/sagerun:$($script:PerfilCleanmgr)" -WindowStyle Hidden -PassThru
    # Se o cleanmgr travar esperando uma janela escondida, a otimizacao segue em frente
    if (-not $processo.WaitForExit(900000)) {
        Stop-Process -Id $processo.Id -Force -ErrorAction SilentlyContinue
        throw "A limpeza de disco passou de 15 minutos e foi interrompida."
    }
}

# ---------------------------------------------------------------
# 3. Otimizacoes basicas (Versao Padrao)
# ---------------------------------------------------------------
function Otimizar-Padrao {
    Iniciar-Snapshot "Versao Padrao"
    Criar-PontoDeRestauracao
    Limpar-Temporarios

    Write-Secao "Aplicando otimizacoes gerais"
    $etapas = @(
        @{ Nome = "Plano de energia Qrz"; Risco = "moderado"; Requer = "desktop"; Acao = {
                if (-not (Pode-AplicarPlanoQrz)) { Write-Host "[INFO] Plano atual mantido neste equipamento."; return }
                Capturar-PlanoEnergia
                Aplicar-PlanoQrz
            } }
        @{ Nome = "Otimizando/TRIM das unidades de disco"; Risco = "seguro"; Acao = {
                Otimizar-Unidades
            } }
        @{ Nome = "Limpando cache DNS"; Risco = "seguro";                  Acao = { ipconfig /flushdns } }
        @{ Nome = "Executando limpeza de disco (cleanmgr)"; Risco = "seguro"; Acao = { Executar-Cleanmgr } }
        @{ Nome = "Desativando sugestoes, anuncios e apps instalados automaticamente"; Risco = "seguro"; Acao = {
                # Impede o Windows de instalar jogos/apps promocionais e mostrar anuncios no Iniciar e no Explorer
                $cdm = "HKCU:\Software\Microsoft\Windows\CurrentVersion\ContentDeliveryManager"
                foreach ($nome in @("SilentInstalledAppsEnabled", "PreInstalledAppsEnabled", "OemPreInstalledAppsEnabled", "SystemPaneSuggestionsEnabled",
                                    "SoftLandingEnabled", "SubscribedContent-338388Enabled", "SubscribedContent-338389Enabled",
                                    "SubscribedContent-353694Enabled", "SubscribedContent-353696Enabled")) {
                    Set-PoliticaDword $cdm $nome 0
                }
                Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" "ShowSyncProviderNotifications" 0
            } }
        @{ Nome = "Limitando o upload de atualizacoes a rede local (Delivery Optimization)"; Risco = "seguro"; Acao = {
                # 1 = compartilha atualizacoes somente com PCs da mesma rede, nunca com a internet
                Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DeliveryOptimization" "DODownloadMode" 1
            } }
        @{ Nome = "Desativando a inicializacao rapida (Fast Startup)"; Risco = "seguro"; Acao = {
                # Com ela ligada o "Desligar" so hiberna o kernel: drivers e atualizacoes nao reiniciam de verdade
                Set-PoliticaDword "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager\Power" "HiberbootEnabled" 0
            } }
        @{ Nome = "Desativando a hibernacao (libera o espaco do hiberfil.sys)"; Risco = "moderado"; Requer = "desktop"; Acao = {
                # Em desktop a hibernacao quase nao e usada e o hiberfil.sys ocupa ate 40% da RAM no disco
                $ligada = (Get-ItemProperty -LiteralPath "HKLM:\SYSTEM\CurrentControlSet\Control\Power" -Name HibernateEnabled -ErrorAction SilentlyContinue).HibernateEnabled
                if ($ligada -eq 0) { Write-Host "[INFO] A hibernacao ja esta desativada; nada a fazer."; return }
                Capturar-Hibernacao
                powercfg /h off
            } }
    )
    Executar-Etapas "Otimizacao Padrao" $etapas
    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "OTIMIZACAO PADRAO CONCLUIDA!") -ForegroundColor Green
    Write-Host (Centralizar "Seu PC deve estar mais leve e rapido.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    if (-not $UiMode) { Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray }
    Linha "="
}

# ---------------------------------------------------------------
# 4. Otimizacoes avancadas (Versao Avancada)
# Cria a chave de registro somente se ela nao existir.
# ATENCAO: "New-Item -Force" em uma chave existente APAGA todos os valores e subchaves dela.
function Garantir-Chave($caminho) {
    if (-not (Test-Path -LiteralPath $caminho)) { New-Item -Path $caminho -Force | Out-Null }
}

# Valores padrao do Windows para as tarefas do agendador multimidia (MMCSS).
# Versoes anteriores do otimizador recriavam a chave SystemProfile e apagavam essas tarefas;
# sem elas, audio e video perdem a prioridade de tempo real (estalos no som, travadas).
$script:TarefasMMCSS = [ordered]@{
    "Audio"                 = @{ Priority = 6; "Scheduling Category" = "Medium"; "Background Only" = "True" }
    "Capture"               = @{ Priority = 5; "Scheduling Category" = "Medium"; "Background Only" = "True" }
    "DisplayPostProcessing" = @{ Priority = 8; "Scheduling Category" = "High";   "Background Only" = "True"; BackgroundPriority = 8 }
    "Distribution"          = @{ Priority = 4; "Scheduling Category" = "Medium"; "Background Only" = "True" }
    "Games"                 = @{ Priority = 2; "Scheduling Category" = "Medium"; "Background Only" = "False" }
    "Playback"              = @{ Priority = 3; "Scheduling Category" = "Medium"; "Background Only" = "False"; BackgroundPriority = 4 }
    "Pro Audio"             = @{ Priority = 1; "Scheduling Category" = "High";   "Background Only" = "False" }
    "Window Manager"        = @{ Priority = 5; "Scheduling Category" = "Medium"; "Background Only" = "True" }
}

# Recria somente o que estiver faltando; valores existentes (inclusive ajustes do usuario) sao mantidos.
function Reparar-MMCSS {
    $base = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
    $reparados = 0
    Garantir-Chave $base
    $raiz = Get-Item -LiteralPath $base
    if ($raiz.GetValueNames() -notcontains "SystemResponsiveness") { Set-PoliticaDword $base "SystemResponsiveness" 20; $reparados++ }
    if ($raiz.GetValueNames() -notcontains "NetworkThrottlingIndex") { Set-PoliticaDword $base "NetworkThrottlingIndex" 10; $reparados++ }
    foreach ($tarefa in $script:TarefasMMCSS.Keys) {
        $caminho = "$base\Tasks\$tarefa"
        Garantir-Chave $caminho
        $existentes = (Get-Item -LiteralPath $caminho).GetValueNames()
        $valores = [ordered]@{ Affinity = 0; "Clock Rate" = 10000; "GPU Priority" = 8; "SFIO Priority" = "Normal" }
        foreach ($par in $script:TarefasMMCSS[$tarefa].GetEnumerator()) { $valores[$par.Key] = $par.Value }
        foreach ($par in $valores.GetEnumerator()) {
            if ($existentes -contains $par.Key) { continue }
            $tipo = if ($par.Value -is [string]) { "String" } else { "DWord" }
            Capturar-Registro $caminho $par.Key
            New-ItemProperty -LiteralPath $caminho -Name $par.Key -Value $par.Value -PropertyType $tipo -ErrorAction Stop | Out-Null
            $reparados++
        }
    }
    if ($reparados -gt 0) { Write-Host "[INFO] Agendador multimidia (MMCSS): $reparados valores padrao do Windows restaurados." }
    else { Write-Host "[INFO] Agendador multimidia (MMCSS): nenhuma correcao necessaria." }
}

function Executar-RepararConfiguracoes {
    Iniciar-Snapshot "Reparar configuracoes"
    Write-Secao "Reparando configuracoes do Windows"
    Reparar-MMCSS
    Write-Host ""
    Linha "="
}

function Set-PoliticaDword($caminho, $nome, $valor) {
    Garantir-Chave $caminho
    Capturar-Registro $caminho $nome
    Set-ItemProperty -Path $caminho -Name $nome -Value $valor -Type DWord -Force -ErrorAction Stop
}

# Muitos PCs ja tiveram servicos removidos por outras ferramentas; isso nao e uma falha.
function Servico-Existe($nome) { return [bool](Get-Service -Name $nome -ErrorAction SilentlyContinue) }

# Otimizacao "sem parar servicos": nenhum servico e parado, desativado ou tem o tipo de inicio mudado
# (PcaSvc, DPS, DiagTrack, SysMain, EventLog e os demais continuam como estao). Funcoes globais com o
# nome dos cmdlets tem prioridade sobre eles, entao toda etapa que mexeria em servico so registra.
function Ativar-ModoSemServicos {
    $script:ModoSemServicos = $true
    Write-Host "[INFO] Otimizacao sem parar servicos: nenhum servico do Windows sera parado ou desativado."
    function global:Stop-Service { param([Parameter(Position = 0)]$Name, [switch]$Force)
        Write-Host "[INFO] Servico mantido em execucao: $Name" }
    function global:Set-Service { param([Parameter(Position = 0)]$Name, $StartupType)
        Write-Host "[INFO] Tipo de inicio mantido: $Name" }
    function global:Suspend-Service { param([Parameter(Position = 0)]$Name)
        Write-Host "[INFO] Servico mantido em execucao: $Name" }
}

function Desativar-Servico($nome) {
    if (-not (Servico-Existe $nome)) { Write-Host "[INFO] Servico $nome nao existe neste Windows; nada a fazer."; return }
    Capturar-Servico $nome
    Stop-Service $nome -Force
    Set-Service $nome -StartupType Disabled
}

# Chamadas SystemParametersInfo: aplicam o ajuste NA HORA e gravam no perfil do usuario
function Carregar-SPI {
    if ('PqoSpi' -as [type]) { return }
    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class PqoSpi {
    [StructLayout(LayoutKind.Sequential)] public struct ANIMATIONINFO { public uint cbSize; public int iMinAnimate; }
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint acao, uint ui, IntPtr pv, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint acao, uint ui, ref ANIMATIONINFO pv, uint flags);
    [DllImport("user32.dll", SetLastError = true)] public static extern bool SystemParametersInfo(uint acao, uint ui, int[] pv, uint flags);
}
"@
}
$script:SPIF_SALVAR = 3  # SPIF_UPDATEINIFILE | SPIF_SENDCHANGE

# O valor VisualFXSetting sozinho so muda a opcao marcada na janela "Opcoes de desempenho";
# os efeitos continuam ligados. Aqui desligamos as animacoes de fato, mantendo a suavizacao
# de fontes (ClearType), as miniaturas e o conteudo da janela ao arrastar.
function Aplicar-EfeitosVisuaisDesempenho {
    $desktop = "HKCU:\Control Panel\Desktop"
    $metricas = "HKCU:\Control Panel\Desktop\WindowMetrics"
    $avancado = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced"
    $dwm = "HKCU:\Software\Microsoft\Windows\DWM"
    $visual = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"
    Capturar-Registro $desktop "UserPreferencesMask"
    Capturar-Registro $metricas "MinAnimate"
    Set-PoliticaDword $avancado "TaskbarAnimations" 0
    Set-PoliticaDword $dwm "EnableAeroPeek" 0
    Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize" "EnableTransparency" 0
    Set-PoliticaDword $visual "VisualFXSetting" 3  # 3 = personalizado (reflete o que foi aplicado)

    Carregar-SPI
    $info = New-Object PqoSpi+ANIMATIONINFO
    $info.cbSize = 8; $info.iMinAnimate = 0
    if (-not [PqoSpi]::SystemParametersInfo(0x0049, 8, [ref]$info, $script:SPIF_SALVAR)) { throw "Falha ao desativar a animacao de janelas" }  # SPI_SETANIMATION
    # Animacao de menus, caixas de combinacao, rolagem suave, fade de menus/selecao/dicas e animacoes do Windows 10/11
    foreach ($acao in 0x1003, 0x1005, 0x1007, 0x1013, 0x1015, 0x1017, 0x1019, 0x1043) {
        [void][PqoSpi]::SystemParametersInfo($acao, 0, [IntPtr]::Zero, $script:SPIF_SALVAR)
    }
}

# Modelos de plano de energia do Windows
$script:PlanoEquilibrado      = "381b4222-f694-41f0-9685-ff5bb260df2e"
$script:PlanoEconomia         = "a1841308-3541-4fab-bc81-f71556f20b4a"
$script:PlanoQrz              = "0a7f1b2c-5172-4e0a-9c11-517a00000001"

function Obter-PlanoAtivo {
    if ((powercfg /getactivescheme) -match "([0-9a-fA-F-]{36})") { return $matches[1] }
    return $null
}

# Ativa uma copia do plano-modelo com o nome do otimizador, reaproveitando a copia de execucoes
# anteriores em vez de duplicar um plano novo a cada vez. Retorna $false quando o Windows nao
# oferece o modelo (ex.: notebooks com Modern Standby so tem o Equilibrado).
# Alto Desempenho do Windows; se outro otimizador ou uma imagem personalizada o apagou, recria pelo modelo
# Ryzen X3D com dois CCDs (7900X3D, 7950X3D, 9900X3D, 9950X3D): o driver da AMD so manda o jogo para o CCD
# com 3D V-Cache quando a Game Bar reconhece o jogo e o plano Equilibrado pode estacionar o outro CCD.
# Planos de desempenho desligam o estacionamento de nucleos e o jogo passa a rodar no CCD sem cache.
function Test-X3dDuploCcd {
    $nome = (Get-ItemProperty "HKLM:\HARDWARE\DESCRIPTION\System\CentralProcessor\0" -Name ProcessorNameString -ErrorAction SilentlyContinue).ProcessorNameString
    return [bool]($nome -match 'Ryzen\s+\d+\s+(7900|7950|9900|9950)X3D')
}

function Pode-AplicarPlanoQrz {
    if (Test-X3dDuploCcd) { return $false }
    try {
        $bateria = Get-CimInstance Win32_Battery -ErrorAction Stop
        return $null -eq $bateria
    } catch { return $false }
}

function Aplicar-PlanoQrz {
    $qrz = $script:PlanoQrz
    # Mesmo arquivo que o app usa (copiado ao lado do script); os testes apontam para o do projeto
    $arquivo = if ($script:ArquivoQrz) { $script:ArquivoQrz } else { Join-Path $PSScriptRoot "Assets\Qrz.powerplan.txt" }
    # Script rodado direto da pasta do projeto: o arquivo fica no projeto do app
    if (-not (Test-Path -LiteralPath $arquivo -PathType Leaf)) { $arquivo = Join-Path $PSScriptRoot "PQueirozOptimizer\Assets\Qrz.powerplan.txt" }
    if (-not (Test-Path -LiteralPath $arquivo -PathType Leaf)) { throw "Configuracao do plano Qrz nao encontrada." }
    $lista = @(powercfg /list)
    if ($lista -match $qrz) {
        # O Windows nao apaga o plano ativo: com o Qrz ja ativo, passa antes para o Equilibrado
        if ((Obter-PlanoAtivo) -eq $qrz) { powercfg /setactive $script:PlanoEquilibrado | Out-Null }
        powercfg /delete $qrz | Out-Null
        if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel remover o plano Qrz anterior." }
    }
    powercfg /duplicatescheme $script:PlanoEquilibrado $qrz | Out-Null
    if ($LASTEXITCODE -ne 0) { throw "Nao foi possivel criar o plano Qrz." }
    powercfg /changename $qrz "Qrz" | Out-Null
    # Algumas configuracoes nao existem em todo hardware (ex.: opcoes de CPU de outro fabricante):
    # nao impedem o plano, mas aparecem no registro em vez de sumir em silencio
    $naoAplicadas = 0
    foreach ($linha in Get-Content -LiteralPath $arquivo) {
        $dados = ($linha -split '#', 2)[0].Trim() -split '\s+'
        if ($dados.Count -ne 4) { continue }
        $sub = $dados[0]; $setting = $dados[1]; $ac = $dados[2]; $dc = $dados[3]
        powercfg /setacvalueindex $qrz $sub $setting $ac | Out-Null
        if ($LASTEXITCODE -ne 0) { $naoAplicadas++ }
        powercfg /setdcvalueindex $qrz $sub $setting $dc | Out-Null
        if ($LASTEXITCODE -ne 0) { $naoAplicadas++ }
    }
    if ($naoAplicadas -gt 0) { Write-Host "[AVISO] Plano Qrz: $naoAplicadas valor(es) nao existem neste PC e ficaram no padrao do Windows." }
    $global:LASTEXITCODE = 0
    powercfg /setactive $qrz | Out-Null
    if ($LASTEXITCODE -ne 0 -or (Obter-PlanoAtivo) -ne $qrz) { throw "Nao foi possivel ativar o plano Qrz." }
}

function Aplicar-PoliticasAvancadas {
    Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection" "AllowTelemetry" 0
    $appCompat = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AppCompat"
    Set-PoliticaDword $appCompat "DisableInventory" 1
    Set-PoliticaDword $appCompat "DisableUAR" 1
    Set-PoliticaDword $appCompat "DisableProblemStepsRecorder" 1
    $cloud = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\CloudContent"
    Set-PoliticaDword $cloud "DisableConsumerAccountStateContent" 1
    Set-PoliticaDword $cloud "DisableCloudOptimizedContent" 1
    Set-PoliticaDword $cloud "DisableWindowsConsumerFeatures" 1
    Set-PoliticaDword $cloud "DisableSoftLanding" 1
    Set-PoliticaDword "HKCU:\Software\Policies\Microsoft\Windows\Explorer" "DisableGraphRecentItems" 1
    Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" "DisableAgenticSearch" 1
    Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" "DisableAIDataAnalysis" 1
    Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\PushToInstall" "DisablePushToInstall" 1
}

# ---------------------------------------------------------------
function Otimizar-Gamer {
    Iniciar-Snapshot "Versao Avancada"
    Criar-PontoDeRestauracao
    Limpar-Temporarios

    Write-Secao "Aplicando otimizacoes de desempenho maximo para jogos"
    $etapas = @(
        @{ Nome = "Verificando agendador multimidia do Windows (MMCSS)"; Risco = "seguro"; Acao = { Reparar-MMCSS } }
        @{ Nome = "Plano de energia Qrz"; Risco = "moderado"; Requer = "desktop"; Acao = {
                if (Test-X3dDuploCcd) { Write-Host "[INFO] Ryzen X3D com dois CCDs: plano Equilibrado mantido para o jogo usar o CCD com 3D V-Cache."; return }
                Capturar-PlanoEnergia
                if (Pode-AplicarPlanoQrz) { Aplicar-PlanoQrz } else { Write-Host "[INFO] Plano atual mantido neste equipamento." }
            } }
        @{ Nome = "Priorizando CPU para o jogo em foco"; Risco = "seguro";  Acao = {
                # 38 (0x26) = quantum curto, variavel, 3x para a janela em foco. No Windows cliente o
                # padrao (2) ja equivale a isso; o ajuste corrige PCs configurados para "servicos em segundo plano".
                $atual = (Get-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" -Name Win32PrioritySeparation -ErrorAction SilentlyContinue).Win32PrioritySeparation
                if ($atual -in 2, 38) { Write-Host "[INFO] O Windows ja prioriza o programa em foco; nenhuma mudanca necessaria."; return }
                Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation"
                Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation" 38 -Type DWord
            } }
        @{ Nome = "Reduzindo latencia de rede/multimidia"; Risco = "moderado"; Acao = {
                $multimediaPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
                Garantir-Chave $multimediaPath
                Capturar-Registro $multimediaPath "SystemResponsiveness"
                Set-ItemProperty $multimediaPath "SystemResponsiveness" 10 -Type DWord  # 10 e o minimo aceito; 0 nao e um valor documentado

                $gamesPath = "$multimediaPath\Tasks\Games"
                Garantir-Chave $gamesPath
                Capturar-Registro $gamesPath "GPU Priority"
                Set-ItemProperty $gamesPath "GPU Priority" 8 -Type DWord
                Capturar-Registro $gamesPath "Priority"
                Set-ItemProperty $gamesPath "Priority" 6 -Type DWord
                Capturar-Registro $gamesPath "Scheduling Category"
                Set-ItemProperty $gamesPath "Scheduling Category" "High" -Type String
                Capturar-Registro $gamesPath "SFIO Priority"
                Set-ItemProperty $gamesPath "SFIO Priority" "High" -Type String
            } }
        @{ Nome = "Desativando limitacao de rede (Throttling)"; Risco = "moderado"; Acao = {
                Capturar-Registro "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex"
                Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex" 0xffffffff -Type DWord
            } }
        @{ Nome = "Desativando gravacao de jogos em segundo plano"; Risco = "moderado"; Acao = {
                if (Test-X3dDuploCcd) { Write-Host "[INFO] Ryzen X3D com dois CCDs: Game Bar mantida (o Windows usa ela para levar o jogo ao CCD com 3D V-Cache)."; return }
                Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR" "AppCaptureEnabled" 0
                Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\GameDVR" "HistoricalCaptureEnabled" 0
                Set-PoliticaDword "HKCU:\System\GameConfigStore" "GameDVR_Enabled" 0
                Set-PoliticaDword "HKCU:\System\GameConfigStore" "GameDVR_HistoricalCaptureEnabled" 0
                Garantir-Chave "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR"
                Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" "AllowGameDVR" 0
            } }
        @{ Nome = "Ativando Modo de Jogo do Windows"; Risco = "seguro";     Acao = {
                Set-PoliticaDword "HKCU:\Software\Microsoft\GameBar" "AllowAutoGameMode" 1
                Set-PoliticaDword "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled" 1
            } }
        @{ Nome = "Habilitando GPU Scheduling por hardware"; Risco = "moderado"; Acao = {
                if (-not (Suporta-GpuScheduling)) {
                    Write-Host "[INFO] GPU/driver sem suporte a agendamento por hardware (HAGS); ajuste ignorado."
                    return
                }
                Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode"
                Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode" 2 -Type DWord
            } }
        @{ Nome = "Ajustando efeitos visuais p/ desempenho"; Risco = "seguro"; Acao = { Aplicar-EfeitosVisuaisDesempenho } }
        @{ Nome = "Desativando aceleracao do ponteiro do mouse"; Risco = "moderado"; Acao = {
                $mousePath = "HKCU:\Control Panel\Mouse"
                Capturar-Registro $mousePath "MouseSpeed"
                Capturar-Registro $mousePath "MouseThreshold1"
                Capturar-Registro $mousePath "MouseThreshold2"
                # SPI_SETMOUSE desliga a "precisao aprimorada do ponteiro" na hora (o registro sozinho so vale no proximo logon)
                Carregar-SPI
                if (-not [PqoSpi]::SystemParametersInfo(0x0004, 0, [int[]](0, 0, 0), $script:SPIF_SALVAR)) {
                    Set-ItemProperty $mousePath "MouseSpeed" "0" -Type String
                    Set-ItemProperty $mousePath "MouseThreshold1" "0" -Type String
                    Set-ItemProperty $mousePath "MouseThreshold2" "0" -Type String
                }
            } }
        @{ Nome = "Limpando cache DNS"; Risco = "seguro";                   Acao = { ipconfig /flushdns } }
        @{ Nome = "Otimizando unidades de disco"; Risco = "seguro";         Acao = {
                Otimizar-Unidades
            } }
        @{ Nome = "Desativando experiencias personalizadas com dados de diagnostico"; Risco = "seguro"; Acao = {
                Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy" "TailoredExperiencesWithDiagnosticDataEnabled" 0
            } }
        @{ Nome = "Aplicando politicas do Editor de Politica de Grupo (diagnostico, nuvem, IA e Push)"; Risco = "moderado"; Acao = { Aplicar-PoliticasAvancadas } }
        @{ Nome = "Ativando otimizacoes para jogos em janela"; Risco = "moderado"; Requer = "win11"; Acao = {
                # Recurso do Windows 11 que reduz a latencia de jogos DirectX 10/11 em janela/borderless
                if ([Environment]::OSVersion.Version.Build -lt 22000) { Write-Host "[INFO] Recurso disponivel apenas no Windows 11; ajuste ignorado."; return }
                $caminho = "HKCU:\Software\Microsoft\DirectX\UserGpuPreferences"
                Garantir-Chave $caminho
                Capturar-Registro $caminho "DirectXUserGlobalSettings"
                $atual = "$((Get-ItemProperty -LiteralPath $caminho -Name DirectXUserGlobalSettings -ErrorAction SilentlyContinue).DirectXUserGlobalSettings)"
                $partes = @($atual -split ';' | Where-Object { $_ -and $_ -notmatch '^SwapEffectUpgradeEnable=' })
                $novo = (($partes + "SwapEffectUpgradeEnable=1") -join ';') + ';'
                Set-ItemProperty -LiteralPath $caminho -Name "DirectXUserGlobalSettings" -Value $novo -Type String
            } }
        @{ Nome = "Desativando limitacao de energia de processos (Power Throttling)"; Risco = "moderado"; Requer = "desktop"; Acao = {
                # Em notebooks isso reduz a bateria; so aplicamos em desktops
                if (Get-CimInstance Win32_Battery -ErrorAction SilentlyContinue) { Write-Host "[INFO] Notebook detectado; Power Throttling mantido para preservar a bateria."; return }
                Set-PoliticaDword "HKLM:\SYSTEM\CurrentControlSet\Control\Power\PowerThrottling" "PowerThrottlingOff" 1
            } }
        @{ Nome = "Desativando o ULPS da placa de video AMD"; Risco = "moderado"; Requer = "amd"; Acao = {
                # ULPS desliga a GPU em repouso profundo; acordar dela causa engasgos e telas pretas em alguns PCs
                $classe = "HKLM:\SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}"
                $ajustados = 0
                foreach ($chave in Get-ChildItem -LiteralPath $classe -ErrorAction SilentlyContinue | Where-Object { $_.PSChildName -match '^\d{4}$' }) {
                    $props = Get-ItemProperty -LiteralPath $chave.PSPath -ErrorAction SilentlyContinue
                    if ("$($props.ProviderName)" -notmatch 'AMD|ATI|Advanced Micro Devices' -or $null -eq $props.EnableUlps) { continue }
                    $caminho = "$classe\$($chave.PSChildName)"
                    Capturar-Registro $caminho "EnableUlps"
                    Set-ItemProperty -LiteralPath $caminho -Name "EnableUlps" -Value 0 -Type DWord -Force -ErrorAction Stop
                    $ajustados++
                }
                if ($ajustados -eq 0) { Write-Host "[INFO] Nenhuma placa AMD com ULPS encontrada; nada a fazer." }
            } }
    )
    Executar-Etapas "Otimizacao Avancada" $etapas
    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "OTIMIZACAO AVANCADA CONCLUIDA!") -ForegroundColor Green
    Write-Host (Centralizar "Reinicie o PC para aplicar todas as mudancas.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    if (-not $UiMode) { Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray }
    Linha "="
}

# ---------------------------------------------------------------
# 5. Debloat (pergunta Sim/Nao para cada etapa, ou tudo de uma vez no modo rapido)
# ---------------------------------------------------------------

# Localiza o desinstalador do OneDrive. No Windows 10 o instalador fica em System32/SysWOW64; no
# Windows 11 o OneDrive e instalado por usuario e so o registro (Uninstall) aponta para ele.
# Retorna $null quando o OneDrive nao esta instalado. Como o caminho do registro do usuario pode ser
# alterado sem privilegios e este script roda como administrador, so aceita OneDriveSetup.exe
# assinado pela Microsoft e somente com os argumentos de desinstalacao.
function Obter-DesinstaladorOneDrive {
    $registrados = @()
    foreach ($chave in @("HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
                         "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe",
                         "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\OneDriveSetup.exe")) {
        $comando = "$((Get-ItemProperty -LiteralPath $chave -Name UninstallString -ErrorAction SilentlyContinue).UninstallString)"
        if ($comando -match '^\s*"?([^"]*\\OneDriveSetup\.exe)"?\s*(.*)$') {
            $exe = $matches[1]; $todosUsuarios = $matches[2] -match '/allusers'
            $registrados += [PSCustomObject]@{ Exe = $exe; Argumentos = $(if ($todosUsuarios) { "/uninstall /allusers" } else { "/uninstall" }) }
        }
    }
    $instalado = $registrados.Count -gt 0 -or (Test-Path -LiteralPath "$env:LOCALAPPDATA\Microsoft\OneDrive\OneDrive.exe") -or
                 (Test-Path -LiteralPath "$env:ProgramFiles\Microsoft OneDrive\OneDrive.exe")
    if (-not $instalado) { return $null }
    # O instalador do Windows vem primeiro: ele continua disponivel para a reversao reinstalar
    $candidatos = @($script:InstaladoresOneDrive | ForEach-Object { [PSCustomObject]@{ Exe = $_; Argumentos = "/uninstall" } }) + $registrados
    foreach ($candidato in $candidatos) {
        if (-not (Test-Path -LiteralPath $candidato.Exe)) { continue }
        $assinatura = Get-AuthenticodeSignature -LiteralPath $candidato.Exe
        if ($assinatura.Status -eq "Valid" -and "$($assinatura.SignerCertificate.Subject)" -match 'O=Microsoft Corporation') { return $candidato }
    }
    throw "Desinstalador do OneDrive nao encontrado ou sem assinatura da Microsoft"
}

function Otimizar-Debloat {
    Iniciar-Snapshot "Debloat"
    Criar-PontoDeRestauracao

    Write-Secao "Debloat do Windows"
    if (-not $UiMode) { Write-Host "  Para cada item abaixo, digite S para aplicar ou N (ou ENTER) para pular." -ForegroundColor Gray }
    if (-not $UiMode) { Write-Host "  Nada e alterado sem sua confirmacao." -ForegroundColor Gray }
    Write-Host ""
    $modoEscolha = if ($UiMode) { 'N' } else { Read-Host "  Prefere aplicar TUDO de uma vez, sem perguntar item por item? (S/N)" }
    $modoRapido = ($modoEscolha -match '^[Ss]')
    if ($modoRapido) {
        Write-Host ""
        Write-Host "  Modo rapido ativado: todas as etapas abaixo serao aplicadas automaticamente." -ForegroundColor Yellow
    }

    # 1. Telemetria e diagnostico
    Executar-Se-Confirmado "Desativar servicos de telemetria e diagnostico (DiagTrack, WerSvc, PcaSvc, etc)?" `
        "Telemetria e diagnostico desativados" `
        {
            # risco: moderado
            $services = @("DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service", "WerSvc", "PcaSvc")
            foreach ($service in $services) {
                Desativar-Servico $service
            }
        } $modoRapido

    # 2. Cortana
    Executar-Se-Confirmado "Remover componentes relacionados a Cortana?" `
        "Cortana removida" `
        {
            # risco: moderado
            Registrar-Irreversivel "Cortana (app removido - reinstale pela Microsoft Store se precisar)"
            Get-AppxPackage -AllUsers -Name "*Microsoft.549981C3F5F10*" | Remove-AppxPackage -AllUsers
        } $modoRapido

    # 3. Widgets / Web Experience
    Executar-Se-Confirmado "Remover Widgets / Windows Web Experience?" `
        "Widgets removidos" `
        {
            # risco: moderado
            # requer: win11
            Registrar-Irreversivel "Widgets/Web Experience (app removido - reinstale pela Microsoft Store se precisar)"
            Get-AppxPackage -AllUsers | Where-Object {
                $_.Name -like "*WebExperience*" -or $_.Name -like "*WindowsWidgets*"
            } | Remove-AppxPackage -AllUsers
        } $modoRapido

    # 4. OneDrive
    Executar-Se-Confirmado "Remover o OneDrive?" `
        "OneDrive removido" `
        {
            # risco: moderado
            $desinstalador = Obter-DesinstaladorOneDrive
            if (-not $desinstalador) { Write-Host "[INFO] OneDrive nao esta instalado; nada a fazer."; return }
            # O instalador do Windows (System32/SysWOW64) continua no sistema e permite reinstalar pela reversao;
            # o do Windows 11 fica dentro da pasta do OneDrive e sai junto com ele.
            if ($script:InstaladoresOneDrive -contains $desinstalador.Exe) { Capturar-OneDrive $desinstalador.Exe }
            else { Registrar-Irreversivel "OneDrive (reinstale pelo site da Microsoft se precisar)" }
            Get-Process OneDrive -ErrorAction SilentlyContinue | Stop-Process -Force
            $processo = Start-Process $desinstalador.Exe -ArgumentList $desinstalador.Argumentos -Wait -PassThru
            if ($processo.ExitCode -ne 0) { throw "O desinstalador do OneDrive retornou codigo $($processo.ExitCode)" }
        } $modoRapido

    # 5. Xbox - mantido por padrao (apenas informativo, sem alteracao)
    Write-Host ""
    Write-Host "   [ i] Xbox/Game Bar: mantido (nao e alterado nesta secao)." -ForegroundColor DarkGray

    # 6. Fax
    Executar-Se-Confirmado "Desativar o servico de Fax?" `
        "Servico de Fax desativado" `
        {
            # risco: seguro
            Desativar-Servico "Fax"
        } $modoRapido

    # 7. Remote Registry
    Executar-Se-Confirmado "Desativar o servico de Registro Remoto (Remote Registry)?" `
        "Remote Registry desativado" `
        {
            # risco: seguro
            Desativar-Servico "RemoteRegistry"
        } $modoRapido

    # 8. Remote Assistance
    Executar-Se-Confirmado "Desativar a Assistencia Remota (Remote Assistance)?" `
        "Assistencia Remota desativada" `
        {
            # risco: seguro
            Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance" "fAllowToGetHelp"
            Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance" "fAllowToGetHelp" -Type DWord -Value 0
        } $modoRapido

    # 9. Mapas
    Executar-Se-Confirmado "Desativar o servico de Mapas (Maps Broker)?" `
        "Servico de Mapas desativado" `
        {
            # risco: seguro
            Desativar-Servico "MapsBroker"
        } $modoRapido

    # 10. Diagnostico
    Executar-Se-Confirmado "Usar somente dados de diagnostico obrigatorios?" `
        "Limitando dados de diagnostico ao nivel obrigatorio" `
        {
            # risco: moderado
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\DataCollection" "AllowTelemetry" 1
        } $modoRapido

    # 13. Tarefas agendadas de telemetria
    Executar-Se-Confirmado "Desativar tarefas opcionais de CEIP e feedback?" `
        "Desativando tarefas opcionais de CEIP e feedback" `
        {
            # risco: seguro
            $tasks = @(
                "\Microsoft\Windows\Customer Experience Improvement Program\Consolidator",
                "\Microsoft\Windows\Customer Experience Improvement Program\UsbCeip",
                "\Microsoft\Windows\Customer Experience Improvement Program\KernelCeipTask",
                "\Microsoft\Windows\Feedback\Siuf\DmClient",
                "\Microsoft\Windows\Feedback\Siuf\DmClientOnScenarioDownload"
            )
            foreach ($task in $tasks) {
                if (-not (Capturar-TarefaAgendada $task)) { Write-Host "[INFO] Tarefa $task nao existe neste Windows; nada a fazer."; continue }
                schtasks.exe /Change /TN $task /Disable | Out-Null
                if ($LASTEXITCODE -ne 0) { throw "Falha ao desativar tarefa: $task" }
            }
        } $modoRapido

    # 14. Apps provisionados desnecessarios
    Executar-Se-Confirmado "Remover apps pre-instalados desnecessarios (Bing News/Weather, Solitaire, Teams, Skype, YourPhone, etc)?" `
        "Apps desnecessarios removidos" `
        {
            # risco: moderado
            Registrar-Irreversivel "Apps pre-instalados removidos (Bing News/Weather, Solitaire, Teams, Skype, YourPhone, etc - reinstale pela Microsoft Store se precisar)"
            $removeApps = @(
                "*BingNews*", "*BingWeather*", "*GetHelp*", "*Getstarted*",
                "*MicrosoftOfficeHub*", "*MicrosoftSolitaireCollection*", "Microsoft.People",
                "*PowerAutomateDesktop*", "*Todos*", "*YourPhone*", "*MicrosoftTeams*",
                "*SkypeApp*", "*MixedReality*", "*WindowsMaps*"
            )
            # As listas sao lidas uma vez: cada consulta leva segundos e antes era repetida para cada app
            $instalados = @(Get-AppxPackage -AllUsers)
            $provisionados = @(Get-AppxProvisionedPackage -Online)
            foreach ($app in $removeApps) {
                $instalados | Where-Object { $_.Name -like $app } | Remove-AppxPackage -AllUsers
                $provisionados | Where-Object { $_.DisplayName -like $app } | Remove-AppxProvisionedPackage -Online
            }
        } $modoRapido

    # 14b. Privacidade: ID de publicidade, experiencias, pesquisa na web, historico e Copilot
    Executar-Se-Confirmado "Desativar o ID de publicidade (anuncios personalizados)?" `
        "ID de publicidade desativado" `
        {
            # risco: seguro
            Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\AdvertisingInfo" "Enabled" 0
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\AdvertisingInfo" "DisabledByGroupPolicy" 1
        } $modoRapido

    Executar-Se-Confirmado "Desativar experiencias personalizadas com dados de diagnostico?" `
        "Desativando experiencias personalizadas com dados de diagnostico" `
        {
            # risco: seguro
            Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\Privacy" "TailoredExperiencesWithDiagnosticDataEnabled" 0
        } $modoRapido

    Executar-Se-Confirmado "Remover resultados da web (Bing) da pesquisa do menu Iniciar?" `
        "Pesquisa na web removida do menu Iniciar" `
        {
            # risco: seguro
            Set-PoliticaDword "HKCU:\Software\Policies\Microsoft\Windows\Explorer" "DisableSearchBoxSuggestions" 1
        } $modoRapido

    Executar-Se-Confirmado "Desativar o historico de atividades (linha do tempo)?" `
        "Historico de atividades desativado" `
        {
            # risco: seguro
            $sistema = "HKLM:\SOFTWARE\Policies\Microsoft\Windows\System"
            Set-PoliticaDword $sistema "EnableActivityFeed" 0
            Set-PoliticaDword $sistema "PublishUserActivities" 0
            Set-PoliticaDword $sistema "UploadUserActivities" 0
        } $modoRapido

    Executar-Se-Confirmado "Desativar o Copilot integrado do Windows?" `
        "Desativando Copilot integrado do Windows" `
        {
            # requer: win11-pre24h2
            # risco: moderado
            if ([Environment]::OSVersion.Version.Build -ge 26100) { Write-Host "[INFO] Copilot integrado nao se aplica a esta versao."; return }
            Set-PoliticaDword "HKCU:\Software\Policies\Microsoft\Windows\WindowsCopilot" "TurnOffWindowsCopilot" 1
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsCopilot" "TurnOffWindowsCopilot" 1
            Set-PoliticaDword "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced" "ShowCopilotButton" 0
        } $modoRapido

    # 14b. Recall (capturas periodicas da tela para a busca com IA)
    Executar-Se-Confirmado "Desativar o Recall (capturas da tela para a IA)?" `
        "Recall desativado" `
        {
            # risco: seguro
            # requer: win11-24h2
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" "DisableAIDataAnalysis" 1
            Set-PoliticaDword "HKCU:\Software\Policies\Microsoft\Windows\WindowsAI" "DisableAIDataAnalysis" 1
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" "AllowRecallEnablement" 0
        } $modoRapido

    # 14c. Click To Do (sobreposicao de IA que analisa o que esta na tela)
    Executar-Se-Confirmado "Desativar o Click To Do?" `
        "Click To Do desativado" `
        {
            # risco: seguro
            # requer: win11-24h2
            Set-PoliticaDword "HKLM:\SOFTWARE\Policies\Microsoft\Windows\WindowsAI" "DisableClickToDo" 1
            Set-PoliticaDword "HKCU:\Software\Policies\Microsoft\Windows\WindowsAI" "DisableClickToDo" 1
        } $modoRapido

    # 15. Efeitos visuais
    Executar-Se-Confirmado "Ajustar efeitos visuais para melhor desempenho?" `
        "Efeitos visuais ajustados" `
        {
            # risco: seguro
            Aplicar-EfeitosVisuaisDesempenho
        } $modoRapido

    # 15. Limpeza final de temporarios
    Executar-Se-Confirmado "Executar limpeza final de arquivos temporarios?" `
        "Arquivos temporarios removidos" `
        {
            # risco: seguro
            if ($UiMode) { throw 'Use a Limpeza Rapida para analisar e selecionar arquivos.' }
            Remove-Item "$(Obter-PastaTempSegura)\*" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:SystemRoot\Temp\*" -Recurse -Force -ErrorAction SilentlyContinue
        } $modoRapido

    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "DEBLOAT CONCLUIDO!") -ForegroundColor Green
    Write-Host (Centralizar "Reinicie o PC para aplicar todas as mudancas.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContPulados pulado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    if (-not $UiMode) { Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray }
    Linha "="
}

# ---------------------------------------------------------------
# 6. Reverter Ultima Otimizacao (desfaz automaticamente a acao 1, 2 ou 3 mais recente)
# ---------------------------------------------------------------
function Reverter-UltimaOtimizacao {
    Write-Secao "Reverter ultima otimizacao"

    if (-not (Test-Path $script:SnapshotPath)) {
        Write-Host "  Nao ha nenhuma otimizacao registrada para reverter no momento." -ForegroundColor Yellow
        Write-Host "  (Isso acontece se voce ainda nao rodou a Padrao/Gamer/Debloat nesta" -ForegroundColor Gray
        Write-Host "   instalacao, ou se a ultima ja foi revertida)." -ForegroundColor Gray
        Write-Host ""
        Linha "="
        return
    }

    $snapshot = $null
    Try {
        $snapshot = Get-Content -Path $script:SnapshotPath -Raw | ConvertFrom-Json
    } Catch {
        Write-Host "  Nao foi possivel ler o registro da ultima otimizacao (arquivo corrompido)." -ForegroundColor Red
        Write-Host ""
        Linha "="
        return
    }

    $itensArray = @($snapshot.Itens)

    Write-Host "  Ultima acao executada: " -NoNewline -ForegroundColor Gray
    Write-Host "$($snapshot.Nome)" -ForegroundColor Cyan
    Write-Host "  Realizada em: $($snapshot.Data)" -ForegroundColor Gray
    Write-Host "  Itens que serao verificados: $($itensArray.Count)" -ForegroundColor Gray

    if (-not $UiMode -and -not (Confirmar "Deseja reverter essa otimizacao agora?")) {
        Write-Host ""
        Write-Host "  Nenhuma alteracao foi feita." -ForegroundColor Yellow
        Write-Host ""
        Linha "="
        return
    }

    Write-Host ""
    $revertidos = 0
    $pendentes = @()
    # Com ajustes escolhidos na tela (SelectedSteps), so os itens deles sao revertidos; o resto continua no backup
    $parcial = $UiMode -and $null -ne $script:SelectedSteps
    $mantidos = @()
    $falhas = 0
    $naoReversiveis = @()

    # Percorre de tras para frente (ordem inversa a de aplicacao)
    for ($i = $itensArray.Count - 1; $i -ge 0; $i--) {
        $item = $itensArray[$i]
        if ($parcial -and $item.Etapa -notin $script:SelectedSteps) { $mantidos = @($item) + $mantidos; continue }
        if (-not (Test-ItemPermitido $item)) {
            Write-Host "[IGNORADO] Item de backup nao reconhecido ou fora da lista permitida: $($item.Tipo) $($item.Caminho) $($item.Nome)"
            continue
        }
        Try {
            switch ($item.Tipo) {
                "Registro" {
                    if ($item.Existia) {
                        $value = $item.ValorAnterior
                        if ($item.TipoAnterior -eq 'Binary') { $value = [byte[]]$value }
                        if ($item.TipoAnterior -eq 'MultiString') { $value = [string[]]$value }
                        if ($item.TipoAnterior) {
                            New-ItemProperty -Path $item.Caminho -Name $item.Nome -Value $value -PropertyType $item.TipoAnterior -Force -ErrorAction Stop | Out-Null
                        } else { Set-ItemProperty -Path $item.Caminho -Name $item.Nome -Value $value -ErrorAction Stop }
                    } else {
                        if ((Get-ItemProperty -Path $item.Caminho -ErrorAction Stop).PSObject.Properties.Name -contains $item.Nome) {
                            Remove-ItemProperty -Path $item.Caminho -Name $item.Nome -ErrorAction Stop
                        }
                    }
                    Write-Resultado $true "Registro restaurado: $($item.Nome)"
                    $revertidos++
                }
                "Servico" {
                    if ($item.StartupTypeAnterior -eq "AutomaticDelayedStart") {
                        # Set-Service do PowerShell 5.1 nao aceita inicio atrasado; o sc.exe aplica
                        Set-Service -Name $item.Nome -StartupType Automatic -ErrorAction Stop
                        sc.exe config $item.Nome start= delayed-auto | Out-Null
                        if ($LASTEXITCODE -ne 0) { throw "Falha no sc.exe: $LASTEXITCODE" }
                    } else {
                        Set-Service -Name $item.Nome -StartupType $item.StartupTypeAnterior -ErrorAction Stop
                    }
                    if ($item.StatusAnterior -eq "Running") {
                        Start-Service -Name $item.Nome -ErrorAction Stop
                    } elseif ($item.StatusAnterior -eq 'Stopped') {
                        Stop-Service -Name $item.Nome -Force -ErrorAction Stop
                    }
                    Write-Resultado $true "Servico restaurado: $($item.Nome)"
                    $revertidos++
                }
                "PlanoEnergia" {
                    $guid = [guid]::Empty
                    if (-not [guid]::TryParse("$($item.GuidAnterior)", [ref]$guid) -or $guid -eq [guid]::Empty) { throw "GUID de plano invalido." }
                    powercfg /setactive $guid
                    if ($LASTEXITCODE -ne 0 -or (Obter-PlanoAtivo) -ne $guid.ToString()) { throw "O plano anterior nao foi restaurado." }
                    Write-Resultado $true "Plano de energia restaurado"
                    $revertidos++
                }
                "TarefaAgendada" {
                    $state = if ($item.HabilitadaAnterior -eq $false) { '/Disable' } else { '/Enable' }
                    schtasks.exe /Change /TN $item.Nome $state | Out-Null
                    if ($LASTEXITCODE -ne 0) { throw "Falha no schtasks: $LASTEXITCODE" }
                    Write-Resultado $true "Tarefa reativada: $($item.Nome)"
                    $revertidos++
                }
                "OneDrive" {
                    if (Test-Path $item.Caminho) {
                        $installer = Start-Process $item.Caminho -Wait -PassThru -ErrorAction Stop
                        if ($installer.ExitCode -ne 0) { throw "Falha na reinstalacao: $($installer.ExitCode)" }
                        Write-Resultado $true "OneDrive: reinstalacao iniciada"
                        $revertidos++
                    } else {
                        throw 'OneDrive: instalador nao encontrado'
                    }
                }
                "Hibernacao" {
                    powercfg /h on
                    if ($LASTEXITCODE -ne 0) { throw "Falha no powercfg: $LASTEXITCODE" }
                    Write-Resultado $true "Hibernacao religada"
                    $revertidos++
                }
                "Irreversivel" {
                    $naoReversiveis += $item.Descricao
                }
                default { }
            }
        } Catch {
            Write-Resultado $false "Falha ao reverter: $($item.Nome)"
            $falhas++
            $pendentes = @($item) + $pendentes
        }
    }

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "REVERSAO CONCLUIDA") -ForegroundColor Green
    Write-Host (Centralizar "$revertidos item(ns) revertido(s), $falhas falha(s)") -ForegroundColor Gray

    if ($naoReversiveis.Count -gt 0) {
        Write-Host ""
        Write-Host "  Os itens abaixo fazem parte da otimizacao revertida, mas NAO podem" -ForegroundColor Yellow
        Write-Host "  ser desfeitos automaticamente:" -ForegroundColor Yellow
        foreach ($desc in ($naoReversiveis | Select-Object -Unique)) {
            Write-Host "   - $desc" -ForegroundColor DarkYellow
        }
        Write-Host ""
        Write-Host "  Arquivos excluidos exigem backup proprio; apps podem exigir reinstalacao." -ForegroundColor Gray
    }

    $script:ContFalhas += $falhas
    # O que nao foi revertido (falhou ou nao foi escolhido) continua no backup, na ordem original
    $restantes = @($itensArray | Where-Object { $mantidos -contains $_ -or $pendentes -contains $_ })

    # Arquiva o backup como estava antes desta reversao, para nao reverter a mesma acao duas vezes
    Try {
        $arquivoRevertido = Join-Path $script:SnapshotDir "revertido_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        Copy-Item -LiteralPath $script:SnapshotPath -Destination $arquivoRevertido -Force -ErrorAction Stop
        if ($restantes.Count -gt 0) {
            $script:SnapshotNome = $snapshot.Nome
            $script:SnapshotAtual = $restantes
            Salvar-Snapshot
        } else { Remove-Item -LiteralPath $script:SnapshotPath -Force -ErrorAction Stop }
        Limpar-HistoricoAntigo
    } Catch {
        Write-Host "[AVISO] Nao foi possivel arquivar o backup revertido: $($_.Exception.Message)"
    }

    Write-Host ""
    Linha "="
}

# ---------------------------------------------------------------
# 7. Funcoes auxiliares de diagnostico
# ---------------------------------------------------------------
function Test-PendingReboot {
    $paths = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Component Based Servicing\RebootPending",
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\WindowsUpdate\Auto Update\RebootRequired"
    )
    foreach ($p in $paths) {
        if (Test-Path $p) { return $true }
    }
    $pfro = Get-ItemProperty -Path "HKLM:\SYSTEM\CurrentControlSet\Control\Session Manager" -Name "PendingFileRenameOperations" -ErrorAction SilentlyContinue
    if ($pfro) { return $true }
    return $false
}

# ---------------------------------------------------------------
# 8. Gerenciar Inicializacao
# ---------------------------------------------------------------
$script:InicializacaoAprovada = "Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved"

# Itens desativados pelo Gerenciador de Tarefas (ou pela pagina Inicializacao do app) continuam na
# chave Run, mas ficam marcados em StartupApproved: primeiro byte impar = desativado.
function Test-InicializacaoAtiva($aprovado, $nome) {
    if (-not $aprovado) { return $true }
    $valor = $aprovado.GetValue($nome)
    return -not ($valor -is [byte[]] -and $valor.Length -gt 0 -and ($valor[0] -band 1))
}

# Programas nas chaves Run que ainda abrem com o Windows
function Obter-ItensInicializacao {
    $locais = @(
        @{ Caminho = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Usuario"; Aprovado = "HKCU:\$script:InicializacaoAprovada\Run" },
        @{ Caminho = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Maquina"; Aprovado = "HKLM:\$script:InicializacaoAprovada\Run" },
        @{ Caminho = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Maquina (32-bit)"; Aprovado = "HKLM:\$script:InicializacaoAprovada\Run32" }
    )
    $itens = @()
    foreach ($local in $locais) {
        if (-not (Test-Path $local.Caminho)) { continue }
        $chave = Get-Item -LiteralPath $local.Caminho
        $aprovado = Get-Item -LiteralPath $local.Aprovado -ErrorAction SilentlyContinue
        foreach ($nome in $chave.GetValueNames() | Where-Object { $_ }) {
            if (-not (Test-InicializacaoAtiva $aprovado $nome)) { continue }
            $itens += [PSCustomObject]@{
                Nome    = $nome
                Comando = "$($chave.GetValue($nome))"
                Caminho = $local.Caminho
                Escopo  = $local.Escopo
            }
        }
    }
    return $itens
}

# Mesma contagem da pagina Inicializacao do app: chaves Run e pastas Inicializar, sem os desativados
function Contar-InicializacaoAtiva {
    $total = @(Obter-ItensInicializacao).Count
    $pastas = @(
        @{ Pasta = [Environment]::GetFolderPath("Startup"); Aprovado = "HKCU:\$script:InicializacaoAprovada\StartupFolder" },
        @{ Pasta = [Environment]::GetFolderPath("CommonStartup"); Aprovado = "HKLM:\$script:InicializacaoAprovada\StartupFolder" }
    )
    foreach ($local in $pastas) {
        if (-not $local.Pasta -or -not (Test-Path -LiteralPath $local.Pasta)) { continue }
        $aprovado = Get-Item -LiteralPath $local.Aprovado -ErrorAction SilentlyContinue
        $total += @(Get-ChildItem -LiteralPath $local.Pasta -File -Force -ErrorAction SilentlyContinue |
                    Where-Object { $_.Name -ne "desktop.ini" -and (Test-InicializacaoAtiva $aprovado $_.Name) }).Count
    }
    return $total
}


# Estimativa de impacto no boot para nomes conhecidos (heuristica por palavra-chave).
# Nao mede tempo real de boot por programa - e uma classificacao aproximada, so para
# ajudar o usuario a decidir o que desativar primeiro.
$script:ImpactoConhecido = @{
    "discord"      = "Alto"
    "steam"        = "Medio"
    "epicgames"    = "Alto"
    "teams"        = "Alto"
    "onedrive"     = "Medio"
    "spotify"      = "Baixo"
    "skype"        = "Medio"
    "dropbox"      = "Medio"
    "adobe"        = "Alto"
    "creative cloud" = "Alto"
    "cortana"      = "Medio"
    "cclient"      = "Baixo"
    "razer"        = "Medio"
    "logitech"     = "Medio"
    "nvidia"       = "Baixo"
    "realtek"      = "Baixo"
    "java"         = "Baixo"
    "quicktime"    = "Baixo"
}

function Obter-ImpactoInicializacao($nome, $comando) {
    $texto = "$nome $comando".ToLower()
    foreach ($chave in $script:ImpactoConhecido.Keys) {
        if ($texto -like "*$chave*") { return $script:ImpactoConhecido[$chave] }
    }
    return "Medio"
}

function Cor-Impacto($impacto) {
    switch ($impacto) {
        "Alto"  { return "Red" }
        "Medio" { return "Yellow" }
        "Baixo" { return "Green" }
        default { return "White" }
    }
}

# Mostra a lista numerada de itens de inicializacao, com o nivel de impacto estimado ao lado
function Mostrar-ListaInicializacao($itens) {
    Write-Host ""
    Write-Host "        IMPACTO" -ForegroundColor DarkGray
    for ($i = 0; $i -lt $itens.Count; $i++) {
        $num = $i + 1
        $item = $itens[$i]
        $impacto = Obter-ImpactoInicializacao $item.Nome $item.Comando
        Write-Host ("  [{0,2}] " -f $num) -NoNewline -ForegroundColor Cyan
        Write-Host ("{0,-27}" -f $item.Nome) -NoNewline -ForegroundColor White
        Write-Host ("[{0,-5}] " -f $impacto) -NoNewline -ForegroundColor (Cor-Impacto $impacto)
        Write-Host ("({0})" -f $item.Escopo) -ForegroundColor DarkGray
        $comandoResumido = $item.Comando
        if ($comandoResumido.Length -gt 66) { $comandoResumido = $comandoResumido.Substring(0, 63) + "..." }
        Write-Host ("        $comandoResumido") -ForegroundColor DarkGray
    }
    Write-Host ""
}

function Desativar-ItensInicializacao($itens) {
    Write-Host "  Digite os numeros dos itens que deseja DESATIVAR, separados por virgula" -ForegroundColor Gray
    Write-Host "  (ex: 1,3,5) ou pressione ENTER para voltar." -ForegroundColor Gray
    Write-Host ""
    $escolha = Read-Host "  Itens para desativar"

    if ([string]::IsNullOrWhiteSpace($escolha)) {
        Write-Host ""
        Write-Host "  Nenhuma alteracao feita." -ForegroundColor Yellow
        return
    }

    $backupDir = "$env:ProgramData\OtimizadorPC\Backups"
    New-Item -Path $backupDir -ItemType Directory -Force | Out-Null
    $backupFile = Join-Path $backupDir "startup_backup_$(Get-Date -Format 'yyyyMMdd_HHmmss').txt"

    $numeros = $escolha -split "," | ForEach-Object { $_.Trim() } | Where-Object { $_ -match '^\d+$' }

    Write-Host ""
    foreach ($num in $numeros) {
        $idx = [int]$num - 1
        if ($idx -ge 0 -and $idx -lt $itens.Count) {
            $item = $itens[$idx]
            Try {
                "Caminho=$($item.Caminho)|Nome=$($item.Nome)|Valor=$($item.Comando)" | Out-File -FilePath $backupFile -Append -Encoding UTF8
                Remove-ItemProperty -Path $item.Caminho -Name $item.Nome -ErrorAction Stop
                Write-Resultado $true "Desativado: $($item.Nome)"
            } Catch {
                Write-Resultado $false "Nao foi possivel desativar: $($item.Nome)"
            }
        }
    }

    Write-Host ""
    Write-Host "  Backup salvo em:" -ForegroundColor DarkGray
    Write-Host "  $backupFile" -ForegroundColor DarkGray
    Write-Host "  (use a opcao [2] Reativar itens para restaurar a partir de um backup)" -ForegroundColor DarkGray
}

function Reativar-ItensInicializacao {
    $backupDir = "$env:ProgramData\OtimizadorPC\Backups"
    if (-not (Test-Path $backupDir)) {
        Write-Host ""
        Write-Host "  Nenhum backup de inicializacao encontrado ainda." -ForegroundColor Yellow
        return
    }
    $backups = Get-ChildItem -Path $backupDir -Filter "startup_backup_*.txt" | Sort-Object LastWriteTime -Descending
    if ($backups.Count -eq 0) {
        Write-Host ""
        Write-Host "  Nenhum backup de inicializacao encontrado ainda." -ForegroundColor Yellow
        return
    }

    Write-Host ""
    Write-Host "  Backups disponiveis (mais recente primeiro):" -ForegroundColor Gray
    for ($i = 0; $i -lt [Math]::Min(10, $backups.Count); $i++) {
        Write-Host ("  [{0,2}] " -f ($i + 1)) -NoNewline -ForegroundColor Cyan
        Write-Host ("$($backups[$i].LastWriteTime)  ($($backups[$i].Name))") -ForegroundColor White
    }
    Write-Host ""
    $escolha = Read-Host "  Qual backup deseja restaurar? (numero, ou ENTER para cancelar)"
    if ([string]::IsNullOrWhiteSpace($escolha) -or -not ($escolha -match '^\d+$')) {
        Write-Host "  Cancelado." -ForegroundColor Yellow
        return
    }
    $idx = [int]$escolha - 1
    if ($idx -lt 0 -or $idx -ge $backups.Count) {
        Write-Host "  Backup invalido." -ForegroundColor Red
        return
    }

    $linhas = Get-Content -Path $backups[$idx].FullName
    Write-Host ""
    foreach ($linha in $linhas) {
        if ($linha -match 'Caminho=(.*?)\|Nome=(.*?)\|Valor=(.*)$') {
            $caminho = $matches[1]; $nome = $matches[2]; $valor = $matches[3]
            if ($script:RunKeysPermitidas -notcontains $caminho) {
                Write-Resultado $false "Ignorado (local de inicializacao nao permitido): $nome"
                continue
            }
            Try {
                if (-not (Test-Path $caminho)) { New-Item -Path $caminho -Force | Out-Null }
                Set-ItemProperty -Path $caminho -Name $nome -Value $valor -Type String -ErrorAction Stop
                Write-Resultado $true "Reativado: $nome"
            } Catch {
                Write-Resultado $false "Nao foi possivel reativar: $nome"
            }
        }
    }
}

function Detalhar-ItemInicializacao($itens) {
    Write-Host ""
    $escolha = Read-Host "  Numero do item para ver detalhes"
    if (-not ($escolha -match '^\d+$')) { return }
    $idx = [int]$escolha - 1
    if ($idx -lt 0 -or $idx -ge $itens.Count) {
        Write-Host "  Item invalido." -ForegroundColor Red
        return
    }
    $item = $itens[$idx]
    $impacto = Obter-ImpactoInicializacao $item.Nome $item.Comando
    Write-Host ""
    Write-Host "  Nome:     " -NoNewline -ForegroundColor DarkGray
    Write-Host $item.Nome -ForegroundColor White
    Write-Host "  Escopo:   " -NoNewline -ForegroundColor DarkGray
    Write-Host $item.Escopo -ForegroundColor White
    Write-Host "  Impacto:  " -NoNewline -ForegroundColor DarkGray
    Write-Host $impacto -ForegroundColor (Cor-Impacto $impacto)
    Write-Host "  Chave:    " -NoNewline -ForegroundColor DarkGray
    Write-Host $item.Caminho -ForegroundColor DarkGray
    Write-Host "  Comando:  " -NoNewline -ForegroundColor DarkGray
    Write-Host $item.Comando -ForegroundColor Gray
}

function Gerenciar-Inicializacao {
    $continuarMenu = $true
    while ($continuarMenu) {
        Write-Secao "Programas de Inicializacao"

        $itens = @(Obter-ItensInicializacao)

        if ($itens.Count -eq 0) {
            Write-Host "  Nenhum programa de inicializacao encontrado." -ForegroundColor Yellow
            Write-Host ""
            Linha "="
            return
        }

        Write-Host "  Estes programas abrem automaticamente junto com o Windows:" -ForegroundColor Gray
        Mostrar-ListaInicializacao $itens

        Write-Host "  [1] Desativar itens   [2] Reativar itens   [3] Ver detalhes   [4] Voltar" -ForegroundColor Cyan
        $opcao = Read-Host "  Escolha uma opcao"

        switch ($opcao) {
            "1" { Desativar-ItensInicializacao $itens }
            "2" { Reativar-ItensInicializacao }
            "3" { Detalhar-ItemInicializacao $itens }
            "4" { $continuarMenu = $false }
            default { Write-Host "  Opcao invalida." -ForegroundColor Red }
        }
        if ($continuarMenu) {
            Write-Host ""
            Read-Host "  Pressione ENTER para continuar"
        }
    }
    Write-Host ""
    Linha "="
}

# ---------------------------------------------------------------
# 9. Analisar PC (diagnostico + PC Health Score, nao altera nada)
# ---------------------------------------------------------------

# Soma o que a Limpeza rapida do app consegue liberar (mesmo criterio: TEMP do usuario e do Windows,
# so arquivos com mais de 48 horas), para a recomendacao "libere X" corresponder ao resultado real.
# O Prefetch fica de fora: o Windows o usa para abrir programas mais rapido.
function Obter-TamanhoTemporarios {
    $pastas = @("$env:TEMP", "$env:SystemRoot\Temp")
    $limite = (Get-Date).AddDays(-2)
    $totalBytes = 0
    foreach ($pasta in $pastas) {
        Try {
            $soma = (Get-ChildItem -Path $pasta -Recurse -Force -File -ErrorAction SilentlyContinue |
                     Where-Object { $_.LastWriteTime -lt $limite } |
                     Measure-Object -Property Length -Sum).Sum
            if ($soma) { $totalBytes += $soma }
        } Catch { }
    }
    return $totalBytes
}

# Detecta se a unidade do sistema e um SSD (usado pela Otimizacao Inteligente)
# Retorna "SSD", "HDD" ou "Desconhecido" para a unidade informada (ex: "C")
function Obter-TipoMidia($letra) {
    Try {
        $particao = Get-Partition -DriveLetter $letra -ErrorAction Stop
        $disco = Get-PhysicalDisk -ErrorAction Stop | Where-Object { "$($_.DeviceId)" -eq "$($particao.DiskNumber)" } | Select-Object -First 1
        switch ("$($disco.MediaType)") {
            "SSD" { return "SSD" }
            "HDD" { return "HDD" }
        }
    } Catch { }
    return "Desconhecido"
}

# TRIM somente em SSDs fixos. HDs e unidades removiveis/virtuais sao ignorados,
# porque -ReTrim falha nelas e desfragmentar pode levar horas.
function Otimizar-Unidades {
    $volumes = Get-Volume | Where-Object { $_.DriveLetter -and $_.DriveType -eq "Fixed" -and $_.FileSystem -eq "NTFS" }
    foreach ($volume in $volumes) {
        $tipo = Obter-TipoMidia $volume.DriveLetter
        if ($tipo -eq "SSD") {
            Optimize-Volume -DriveLetter $volume.DriveLetter -ReTrim -ErrorAction Stop
            Write-Host "[INFO] TRIM executado em $($volume.DriveLetter):"
        } else {
            Write-Host "[INFO] $($volume.DriveLetter): ignorada ($tipo) - o Windows ja agenda a otimizacao desta unidade."
        }
    }
}

# HAGS exige Windows 10 2004+ e um driver de video que declare suporte (WDDM 2.7+).
# O dxdiag informa isso diretamente na linha "Hardware Scheduling".
function Suporta-GpuScheduling {
    if ([Environment]::OSVersion.Version.Build -lt 19041) { return $false }
    $relatorio = Join-Path $env:TEMP ("pqo-dxdiag-{0}.txt" -f [Guid]::NewGuid().ToString('N'))
    Try {
        $proc = Start-Process dxdiag.exe -ArgumentList "/whql:off", "/t", "`"$relatorio`"" -WindowStyle Hidden -PassThru
        if (-not $proc.WaitForExit(90000)) { $proc.Kill(); return $false }
        for ($i = 0; $i -lt 20 -and -not (Test-Path -LiteralPath $relatorio); $i++) { Start-Sleep -Milliseconds 250 }
        $texto = Get-Content -LiteralPath $relatorio -Raw -ErrorAction Stop
        $linhas = [regex]::Matches($texto, 'Hardware Scheduling:\s*(.+)') | ForEach-Object { $_.Groups[1].Value }
        if ($linhas) { return [bool]($linhas | Where-Object { $_ -match 'DriverSupportState:(Stable|Supported|Experimental)' }) }
        $modelos = [regex]::Matches($texto, 'Driver Model:\s*WDDM\s*(\d+)\.(\d+)') | ForEach-Object { [version]"$($_.Groups[1].Value).$($_.Groups[2].Value)" }
        return [bool]($modelos | Where-Object { $_ -ge [version]"2.7" })
    } Catch { return $false }
    Finally { Remove-Item -LiteralPath $relatorio -Force -ErrorAction SilentlyContinue }
}

function Detectar-SSD {
    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $disco = Get-PhysicalDisk -ErrorAction Stop | Where-Object {
            (Get-Partition -DiskNumber $_.DeviceId -ErrorAction SilentlyContinue | Get-Volume -ErrorAction SilentlyContinue).DriveLetter -eq $letra
        } | Select-Object -First 1
        if ($disco) { return ($disco.MediaType -eq "SSD") }
    } Catch { }
    return $true  # assume SSD se nao for possivel detectar (mais comum hoje em dia)
}

# Placas de video PCI (dedicadas ou integradas) e o estado atual do modo MSI de cada uma
function Obter-GpusPCI {
    $lista = @()
    Try {
        $dispositivos = Get-PnpDevice -Class Display -PresentOnly -ErrorAction Stop | Where-Object { $_.InstanceId -like 'PCI\VEN_*' }
        foreach ($d in $dispositivos) {
            $caminho = "HKLM:\SYSTEM\CurrentControlSet\Enum\$($d.InstanceId)\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties"
            $valor = $null
            Try { $valor = (Get-ItemProperty -LiteralPath $caminho -Name MSISupported -ErrorAction Stop).MSISupported } Catch { }
            $lista += [PSCustomObject]@{ Nome = $d.FriendlyName; CaminhoMSI = $caminho; MSI = ($valor -eq 1) }
        }
    } Catch { }
    return $lista
}

# Mede por alguns segundos quanto da CPU vai para interrupcoes e DPCs (chamadas adiadas de drivers).
# Valores altos causam travadas, estalos no audio e input lag mesmo com FPS alto.
# Usa as classes WMI (nomes em ingles em qualquer idioma do Windows) em vez do Get-Counter.
# Usa os contadores brutos (a classe "Formatted" arredonda para inteiro e mostraria 0%).
function Medir-LatenciaDPC($segundos = 5) {
    Try {
        $filtro = "Name='_Total'"
        $a = Get-CimInstance Win32_PerfRawData_PerfOS_Processor -Filter $filtro -ErrorAction Stop
        Start-Sleep -Seconds $segundos
        $b = Get-CimInstance Win32_PerfRawData_PerfOS_Processor -Filter $filtro -ErrorAction Stop
        $intervalo = [double]($b.Timestamp_Sys100NS - $a.Timestamp_Sys100NS)
        if ($intervalo -le 0) { return $null }
        $dpc = [Math]::Round(100 * ($b.PercentDPCTime - $a.PercentDPCTime) / $intervalo, 2)
        $irq = [Math]::Round(100 * ($b.PercentInterruptTime - $a.PercentInterruptTime) / $intervalo, 2)
        $porSegundo = [Math]::Round(($b.InterruptsPersec - $a.InterruptsPersec) / ($intervalo / 1e7))
        $nivel = if (($dpc + $irq) -ge 5) { "bad" } elseif (($dpc + $irq) -ge 2) { "warn" } else { "ok" }
        return [PSCustomObject]@{ DPC = $dpc; Interrupcao = $irq; PorSegundo = $porSegundo; Nivel = $nivel }
    } Catch { return $null }
}

# Temperatura dos sensores ACPI da placa-mae (nem todo PC expoe; nao e a temperatura por nucleo da CPU)
function Obter-Temperatura {
    Try {
        $zonas = @(Get-CimInstance Win32_PerfFormattedData_Counters_ThermalZoneInformation -ErrorAction Stop |
                   Where-Object { $_.HighPrecisionTemperature -gt 0 })
        if ($zonas.Count -gt 0) {
            $max = ($zonas | Measure-Object HighPrecisionTemperature -Maximum).Maximum
            $c = [Math]::Round(($max / 10) - 273.15)
            if ($c -gt 0 -and $c -lt 130) { return $c }
        }
    } Catch { }
    Try {
        $zonas = @(Get-CimInstance -Namespace root/wmi -ClassName MSAcpi_ThermalZoneTemperature -ErrorAction Stop)
        if ($zonas.Count -gt 0) {
            $c = [Math]::Round((($zonas | Measure-Object CurrentTemperature -Maximum).Maximum / 10) - 273.15)
            if ($c -gt 0 -and $c -lt 130) { return $c }
        }
    } Catch { }
    return $null
}

# Velocidade nominal do kit de memoria lida do part number (ex.: CMK16GX4M2B3200C16 -> 3200, KF436C16 -> 3600)
function Obter-VelocidadeNominalRAM($partNumber) {
    $pn = "$partNumber".Trim().ToUpper()
    if ($pn -match '^KF(4|5)(\d{2})') { return [int]$matches[2] * 100 }
    if ($pn -match '(?<!\d)(2[4-9]\d{2}|3\d{3}|4\d{3}|[5-8]\d{3})(?!\d)') { return [int]$matches[1] }
    return $null
}

# Levanta o setup do cliente: placa-mae, BIOS, memoria (XMP/EXPO, canais), CPU e timer do sistema.
# Nada disso e alterado pelo otimizador - BIOS/overclock precisam ser feitos manualmente na BIOS.
function Obter-SetupHardware {
    $setup = [ordered]@{
        PlacaMae = "Nao disponivel"; BIOS = "Nao disponivel"; BIOSIdadeAnos = $null
        CPUNucleos = "Nao disponivel"; RAMDetalhe = "Nao disponivel"; RAMModulos = 0
        RAMAtual = $null; RAMNominal = $null; RAMTipo = ""; PlatformClock = $false; HVCI = $false
    }
    Try {
        $placa = Get-CimInstance Win32_BaseBoard -ErrorAction Stop | Select-Object -First 1
        $setup.PlacaMae = ("$($placa.Manufacturer) $($placa.Product)").Trim()
    } Catch { }
    Try {
        $bios = Get-CimInstance Win32_BIOS -ErrorAction Stop | Select-Object -First 1
        $data = $bios.ReleaseDate
        $setup.BIOS = "$($bios.SMBIOSBIOSVersion)".Trim()
        if ($data) {
            $setup.BIOS += " ($($data.ToString('dd/MM/yyyy')))"
            $setup.BIOSIdadeAnos = [Math]::Round(((Get-Date) - $data).TotalDays / 365, 1)
        }
    } Catch { }
    Try {
        $cpu = Get-CimInstance Win32_Processor -ErrorAction Stop | Select-Object -First 1
        $setup.CPUNucleos = "$($cpu.NumberOfCores) nucleos / $($cpu.NumberOfLogicalProcessors) threads - $($cpu.MaxClockSpeed) MHz base"
    } Catch { }
    Try {
        $modulos = @(Get-CimInstance Win32_PhysicalMemory -ErrorAction Stop)
        $setup.RAMModulos = $modulos.Count
        $tipo = switch (($modulos | Select-Object -First 1).SMBIOSMemoryType) { 26 { "DDR4" } 34 { "DDR5" } 24 { "DDR3" } default { "" } }
        $setup.RAMTipo = $tipo
        $atual = ($modulos | Measure-Object ConfiguredClockSpeed -Minimum).Minimum
        $nominais = @($modulos | ForEach-Object { Obter-VelocidadeNominalRAM $_.PartNumber } | Where-Object { $_ })
        $setup.RAMAtual = $atual
        if ($nominais.Count -gt 0) { $setup.RAMNominal = ($nominais | Measure-Object -Minimum).Minimum }
        $totalGB = [Math]::Round((($modulos | Measure-Object Capacity -Sum).Sum) / 1GB)
        $canal = if ($modulos.Count -ge 2) { "dual channel" } else { "single channel" }
        $setup.RAMDetalhe = "$totalGB GB $tipo - $($modulos.Count) pente(s), $canal - $atual MT/s".Replace("  ", " ")
    } Catch { }
    Try {
        # 2 = Integridade de Memoria (HVCI) em execucao
        $dg = Get-CimInstance -Namespace root\Microsoft\Windows\DeviceGuard -ClassName Win32_DeviceGuard -ErrorAction Stop
        $setup.HVCI = @($dg.SecurityServicesRunning) -contains 2
    } Catch { }
    Try {
        $bcd = (bcdedit /enum "{current}") -join "`n"
        $setup.PlatformClock = ($bcd -match '(?im)^useplatformclock\s+(Yes|Sim)')
    } Catch { }
    return $setup
}

# Tenta identificar o "perfil" do PC (trabalho / jogos / geral) olhando programas instalados,
# so para ORIENTAR a Otimizacao Inteligente - nunca decide algo destrutivo sozinho por causa disso
function Detectar-PerfilPC {
    $chavesInstalados = @(
        "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall\*",
        "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall\*"
    )
    $nomes = @()
    foreach ($chave in $chavesInstalados) {
        Try {
            $nomes += Get-ItemProperty -Path $chave -ErrorAction SilentlyContinue |
                      Where-Object { $_.DisplayName } | Select-Object -ExpandProperty DisplayName
        } Catch { }
    }
    $texto = ($nomes -join " | ").ToLower()

    $temTrabalho = ($texto -match "office|outlook|teams|zoom|sap|autocad|visual studio|adobe acrobat|slack")
    $temJogos    = ($texto -match "steam|epic games|riot|battle\.net|origin|gog galaxy|xbox|ubisoft connect")

    if ($temTrabalho -and -not $temJogos) { return "Trabalho" }
    if ($temJogos -and -not $temTrabalho) { return "Jogos" }
    if ($temJogos -and $temTrabalho)      { return "Misto" }
    return "Geral"
}

# Calcula os componentes do "PC Health Score" (0-100 cada) a partir do diagnostico atual.
# E uma ESTIMATIVA para orientar o usuario, nao uma medicao cientifica de desempenho.
function Calcular-HealthScore {
    $resultado = [ordered]@{
        CPU = 70; RAM = 70; Disco = 70; Inicializacao = 70; Windows = 70
        Geral = 70
    }

    Try {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $resultado.CPU = [Math]::Max(0, 100 - [int]$cpu.LoadPercentage)
    } Catch { }

    Try {
        $os = Get-CimInstance Win32_OperatingSystem
        $usadoPercent = [Math]::Round((($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize) * 100)
        $resultado.RAM = [Math]::Max(0, 100 - $usadoPercent)
    } Catch { }

    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $vol = Get-Volume -DriveLetter $letra -ErrorAction Stop
        $livrePercent = [Math]::Round(($vol.SizeRemaining / $vol.Size) * 100)
        $resultado.Disco = [Math]::Min(100, $livrePercent + 20)
    } Catch { }

    Try {
        $resultado.Inicializacao = [Math]::Max(10, 100 - ((Contar-InicializacaoAtiva) * 8))
    } Catch { }

    Try {
        $pontosWindows = 100
        if (Test-PendingReboot) { $pontosWindows -= 15 }
        Try {
            $defender = Get-MpComputerStatus -ErrorAction Stop
            if (-not $defender.RealTimeProtectionEnabled) { $pontosWindows -= 20 }
        } Catch { }
        Try {
            $ultimoPonto = Get-ComputerRestorePoint -ErrorAction Stop
            if (-not $ultimoPonto) { $pontosWindows -= 10 }
        } Catch { }
        $resultado.Windows = [Math]::Max(0, $pontosWindows)
    } Catch { }

    $resultado.Geral = [Math]::Round((
        $resultado.CPU + $resultado.RAM + $resultado.Disco + $resultado.Inicializacao + $resultado.Windows
    ) / 5)

    return $resultado
}

function Barra-Score($valor, $largura = 20) {
    $preenchido = [Math]::Round(($valor / 100) * $largura)
    if ($preenchido -lt 0) { $preenchido = 0 }
    if ($preenchido -gt $largura) { $preenchido = $largura }
    $cor = if ($valor -ge 80) { "Green" } elseif ($valor -ge 50) { "Yellow" } else { "Red" }
    Write-Host ("█" * $preenchido) -NoNewline -ForegroundColor $cor
    Write-Host ("░" * ($largura - $preenchido)) -NoNewline -ForegroundColor DarkGray
    Write-Host (" {0,3}" -f $valor) -ForegroundColor $cor
}

function Mostrar-HealthScore($score) {
    Write-Host ""
    Header-Topo
    Header-Linha "  PC HEALTH SCORE" "" "Yellow" "DarkGray"
    Header-Base
    Write-Host ""
    $corGeral = if ($score.Geral -ge 80) { "Green" } elseif ($score.Geral -ge 50) { "Yellow" } else { "Red" }
    Write-Host (Centralizar "$($score.Geral)/100") -ForegroundColor $corGeral
    Write-Host ""
    Write-Host "  CPU            " -NoNewline -ForegroundColor Gray; Barra-Score $score.CPU
    Write-Host "  RAM            " -NoNewline -ForegroundColor Gray; Barra-Score $score.RAM
    Write-Host "  DISCO          " -NoNewline -ForegroundColor Gray; Barra-Score $score.Disco
    Write-Host "  INICIALIZACAO  " -NoNewline -ForegroundColor Gray; Barra-Score $score.Inicializacao
    Write-Host "  WINDOWS        " -NoNewline -ForegroundColor Gray; Barra-Score $score.Windows
    Write-Host ""
    Write-Host "  (estimativa com base no diagnostico atual - nao e um benchmark)" -ForegroundColor DarkGray
}

function Analisar-PC {
    Write-Secao "Analisando o PC (nao altera nada)"
    Write-Host "  Coletando informacoes..." -ForegroundColor DarkGray

    $info = Obter-InfoResumo
    $ehSSD = Detectar-SSD
    $tipoDisco = if ($ehSSD) { "SSD" } else { "HDD" }
    $tamanhoTemp = Obter-TamanhoTemporarios
    $qtdInicializacao = Contar-InicializacaoAtiva
    $planoAtivo = (powercfg /getactivescheme) -join " "
    $nomePlano = if ($planoAtivo -match '\((.+)\)') { $matches[1] } else { "Desconhecido" }
    # Pelo GUID, como na Versao Padrao: o nome do plano muda com o idioma do Windows ("High performance")
    $planoEconomico = (Obter-PlanoAtivo) -in $script:PlanoEquilibrado, $script:PlanoEconomia
    $searchAtivo = $false
    Try { $searchAtivo = (Get-Service WSearch -ErrorAction Stop).Status -eq "Running" } Catch { }
    $trimAtivo = $true
    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $trimAtivo = -not ((fsutil behavior query DisableDeleteNotify) -match "= 1")
    } Catch { }
    $gameModeAtivo = $false
    Try {
        $gm = Get-ItemProperty -Path "HKCU:\Software\Microsoft\GameBar" -Name "AutoGameModeEnabled" -ErrorAction Stop
        $gameModeAtivo = ($gm.AutoGameModeEnabled -eq 1)
    } Catch { }

    Write-Host ""
    Write-Host "  CPU           " -NoNewline -ForegroundColor DarkGray; Write-Host $info.CPU -ForegroundColor Cyan
    Write-Host "  RAM           " -NoNewline -ForegroundColor DarkGray; Write-Host $info.RAM -ForegroundColor Cyan
    Write-Host "  GPU           " -NoNewline -ForegroundColor DarkGray; Write-Host $info.GPU -ForegroundColor Cyan
    Write-Host "  Disco         " -NoNewline -ForegroundColor DarkGray; Write-Host "$tipoDisco - $($info.Disco)" -ForegroundColor Cyan

    $setup = Obter-SetupHardware
    $gpus = @(Obter-GpusPCI)
    $temperatura = Obter-Temperatura
    Write-Host "  Medindo latencia DPC e interrupcoes (5 s)..." -ForegroundColor DarkGray
    $latencia = Medir-LatenciaDPC 5

    Write-Host ""
    Write-Host "  Placa-mae     " -NoNewline -ForegroundColor DarkGray; Write-Host $setup.PlacaMae -ForegroundColor Cyan
    Write-Host "  BIOS          " -NoNewline -ForegroundColor DarkGray; Write-Host $setup.BIOS -ForegroundColor Cyan
    Write-Host "  Nucleos       " -NoNewline -ForegroundColor DarkGray; Write-Host $setup.CPUNucleos -ForegroundColor Cyan
    Write-Host "  Memoria       " -NoNewline -ForegroundColor DarkGray; Write-Host $setup.RAMDetalhe -ForegroundColor Cyan
    if ($null -ne $temperatura) {
        $corTemp = if ($temperatura -ge 85) { "Red" } elseif ($temperatura -ge 70) { "Yellow" } else { "Cyan" }
        Write-Host "  Temperatura   " -NoNewline -ForegroundColor DarkGray; Write-Host "$temperatura C (sensor ACPI)" -ForegroundColor $corTemp
    }
    if ($latencia) {
        $corLat = Cor-Nivel $latencia.Nivel
        Write-Host "  Latencia DPC  " -NoNewline -ForegroundColor DarkGray
        Write-Host ("DPC {0}% | Interrupcoes {1}% ({2}/s)" -f $latencia.DPC, $latencia.Interrupcao, $latencia.PorSegundo) -ForegroundColor $corLat
    }
    foreach ($gpu in $gpus) {
        $estadoMsi = if ($gpu.MSI) { "ativo" } else { "desativado" }
        Write-Host "  Modo MSI      " -NoNewline -ForegroundColor DarkGray; Write-Host "$($gpu.Nome): $estadoMsi" -ForegroundColor Cyan
    }

    $score = Calcular-HealthScore
    $script:UltimoScore = $score
    Mostrar-HealthScore $score

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "PROBLEMAS ENCONTRADOS") -ForegroundColor Yellow
    Linha "="

    $problemas = @()
    if ($tamanhoTemp -gt 500MB) { $problemas += "$(Format-Bytes $tamanhoTemp) de arquivos temporarios acumulados" }
    if ($qtdInicializacao -ge 5) { $problemas += "$qtdInicializacao programas iniciando junto com o Windows" }
    if ($searchAtivo) { $problemas += "Windows Search ativo (pode consumir recursos em HDs mais lentos)" }
    if ($planoEconomico) { $problemas += "Plano de energia atual: $nomePlano" }
    if (-not $trimAtivo -and $ehSSD) { $problemas += "TRIM parece desativado no SSD" }
    if (Test-PendingReboot) { $problemas += "Ha uma reinicializacao pendente" }
    if ($score.Disco -lt 40) { $problemas += "Pouco espaco livre em disco" }
    if ($latencia -and $latencia.Nivel -ne "ok") { $problemas += "Latencia DPC/interrupcoes alta ($($latencia.DPC + $latencia.Interrupcao)% da CPU)" }
    if (@($gpus | Where-Object { -not $_.MSI }).Count -gt 0) { $problemas += "Placa de video sem modo MSI (interrupcoes por linha IRQ)" }
    if ($setup.PlatformClock) { $problemas += "Timer HPET forcado no boot (useplatformclock) - aumenta a latencia" }
    if ($setup.HVCI) { $problemas += "Integridade de Memoria (HVCI) ativa - pode custar de 5% a 10% de FPS em alguns jogos" }
    if ($null -ne $temperatura -and $temperatura -ge 85) { $problemas += "Temperatura alta: $temperatura C - verifique cooling e pasta termica" }
    if ($setup.RAMModulos -eq 1) { $problemas += "Memoria em single channel (1 pente) - 2 pentes dobram a largura de banda" }
    $ramSemXmp = $false
    if ($setup.RAMNominal -and $setup.RAMAtual -and $setup.RAMAtual -lt ($setup.RAMNominal - 100)) {
        $ramSemXmp = $true
        $problemas += "Memoria rodando a $($setup.RAMAtual) MT/s, mas o kit suporta $($setup.RAMNominal) MT/s (XMP/EXPO desligado na BIOS)"
    }
    if ($setup.BIOSIdadeAnos -and $setup.BIOSIdadeAnos -ge 3) { $problemas += "BIOS com $($setup.BIOSIdadeAnos) anos - pode haver versao mais nova no site da placa-mae" }

    if ($problemas.Count -eq 0) {
        Write-Status "ok" "Nenhum problema relevante encontrado. Seu PC esta em bom estado!"
    } else {
        foreach ($p in $problemas) { Write-Status "warn" $p }
    }
    if ($trimAtivo -and $ehSSD)      { Write-Status "ok" "TRIM: Ativo" }
    if ($gameModeAtivo)              { Write-Status "ok" "Modo de Jogo: Ativo" }

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "RECOMENDACOES") -ForegroundColor Yellow
    Linha "="
    if ($UiMode) {
        # Recomendacoes apontam para as telas do aplicativo, nao para o menu do console
        $recomendou = $false
        if ($tamanhoTemp -gt 500MB) { Write-Status "info" "Use a Limpeza rapida para liberar $(Format-Bytes $tamanhoTemp)"; $recomendou = $true }
        if ($qtdInicializacao -ge 5) { Write-Status "info" "Desative programas desnecessarios na pagina Inicializacao do aplicativo"; $recomendou = $true }
        if ($planoEconomico) { Write-Status "info" "Aplique a Versao Padrao para usar o plano de energia de alto desempenho"; $recomendou = $true }
        if (Test-PendingReboot) { Write-Status "info" "Reinicie o computador para concluir atualizacoes pendentes"; $recomendou = $true }
        if (@($gpus | Where-Object { -not $_.MSI }).Count -gt 0) { Write-Status "info" "Aplique a Versao Avancada para ativar o modo MSI da placa de video"; $recomendou = $true }
        if ($ramSemXmp) { Write-Status "info" "Ative o perfil XMP/EXPO na BIOS (menu BIOS / UEFI > Reiniciar na BIOS/UEFI)"; $recomendou = $true }
        if ($setup.HVCI) { Write-Status "info" "Se o PC e so para jogos, avalie desligar a Integridade de Memoria em Seguranca do Windows > Seguranca do dispositivo (reduz a protecao)"; $recomendou = $true }
        if ($setup.PlatformClock) { Write-Status "info" "Remova o HPET forcado: bcdedit /deletevalue useplatformclock (como administrador)"; $recomendou = $true }
        if (-not $recomendou) { Write-Status "ok" "Nenhuma acao recomendada no momento" }
    } else {
        Write-Host "  [1] Otimizacao Inteligente  - deixa o programa decidir o que aplicar" -ForegroundColor Cyan
        Write-Host "  [2] Versao Padrao           - limpeza e ajustes basicos" -ForegroundColor Cyan
        if ($qtdInicializacao -ge 5) {
            Write-Host "  [8] Gerenciar Inicializacao - reduzir programas no boot" -ForegroundColor Cyan
        }
    }
    Write-Host ""

    if (-not $UiMode -and (Confirmar "Deseja rodar a Otimizacao Inteligente agora com base nesse diagnostico?")) {
        Otimizar-Inteligente
    } else {
        Write-Host ""
        Linha "="
    }
}

# ---------------------------------------------------------------
# 10. Otimizacao Inteligente (analisa o PC e decide o que aplicar)
# ---------------------------------------------------------------
function Otimizar-Inteligente {
    Write-Secao "Otimizacao Inteligente"
    Write-Host "  Analisando o PC antes de decidir o que aplicar..." -ForegroundColor Gray
    Write-Host ""

    $ehSSD  = Detectar-SSD
    $perfil = Detectar-PerfilPC
    Try { $totalRAM = [Math]::Round((Get-CimInstance Win32_OperatingSystem).TotalVisibleMemorySize / 1MB) } Catch { $totalRAM = 8 }

    Write-Host "  Detectado:" -ForegroundColor Cyan
    Write-Status "info" "Disco do sistema: $(if ($ehSSD) { 'SSD' } else { 'HDD' })"
    Write-Status "info" "Memoria RAM: $totalRAM GB"
    Write-Status "info" "Perfil de uso provavel: $perfil"
    Write-Host ""

    Write-Host "  Com base nisso, o que sera feito:" -ForegroundColor Cyan
    Write-Status "ok" "Limpeza de temporarios, cache e lixeira"
    Write-Status "ok" "Otimizacao/TRIM do(s) disco(s)"
    Write-Status "ok" "Limpeza de cache DNS"
    if ($perfil -eq "Jogos" -or $perfil -eq "Misto") {
        Write-Status "ok" "Plano de energia Qrz + Modo de Jogo (perfil com jogos instalados)"
    } else {
        Write-Status "ok" "Plano de energia atual mantido (perfil sem jogos)"
    }
    if ($perfil -eq "Trabalho" -or $perfil -eq "Misto") {
        Write-Status "info" "Windows Search, Bluetooth e Impressao NAO serao tocados (perfil de trabalho detectado)"
    }
    Write-Status "ok" "Verificacao rapida de arquivos do sistema (sfc /verifyonly)"
    Write-Host ""

    if (-not (Confirmar "Aplicar essas otimizacoes agora?")) {
        Write-Host ""
        Write-Host "  Nenhuma alteracao foi feita." -ForegroundColor Yellow
        Write-Host ""
        Linha "="
        return
    }

    Iniciar-Snapshot "Otimizacao Inteligente"
    Criar-PontoDeRestauracao
    Limpar-Temporarios

    Write-Secao "Aplicando otimizacoes decididas automaticamente"
    $etapas = @(
        @{ Nome = "Plano de energia Qrz"; Risco = "moderado"; Requer = "desktop"; Acao = {
                if (Test-X3dDuploCcd) { Write-Host "[INFO] Ryzen X3D com dois CCDs: plano Equilibrado mantido para o jogo usar o CCD com 3D V-Cache."; return }
                Capturar-PlanoEnergia
                if (($perfil -eq "Jogos" -or $perfil -eq "Misto") -and (Pode-AplicarPlanoQrz)) { Aplicar-PlanoQrz }
            } }
        @{ Nome = "Limpando cache DNS"; Risco = "seguro"; Acao = { ipconfig /flushdns } }
        @{ Nome = "Otimizando/TRIM das unidades de disco"; Risco = "seguro"; Acao = {
                Otimizar-Unidades
            } }
        @{ Nome = "Limpeza de disco (cleanmgr)"; Risco = "seguro"; Acao = { Executar-Cleanmgr } }
    )
    if ($perfil -eq "Jogos" -or $perfil -eq "Misto") {
        $etapas += @{ Nome = "Ativando Modo de Jogo do Windows"; Risco = "seguro"; Acao = {
                Set-PoliticaDword "HKCU:\Software\Microsoft\GameBar" "AllowAutoGameMode" 1
                Set-PoliticaDword "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled" 1
            } }
    }
    $etapas += @{ Nome = "Verificando arquivos do sistema (sfc /verifyonly)"; Risco = "seguro"; Acao = { sfc /verifyonly } }

    Executar-Etapas "Otimizacao Inteligente" $etapas
    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "OTIMIZACAO INTELIGENTE CONCLUIDA!") -ForegroundColor Green
    Write-Host (Centralizar "Perfil considerado: $perfil") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    Write-Host (Centralizar "Nao gostou? Use a opcao Reverter Ultima Otimizacao.") -ForegroundColor DarkGray
    Linha "="
}

# ---------------------------------------------------------------
# 11. Manutencao do Windows (SFC / DISM / CHKDSK / Windows Update)
# ---------------------------------------------------------------
# Executa um comando do Windows lendo a saida na codificacao certa e repassando linha a linha.
# sfc escreve em UTF-16 e chkdsk/DISM na pagina de codigo OEM; lidos como UTF-8, viram texto quebrado.
# Linhas de progresso ("12% concluido") so sao repassadas a cada 10% para nao inundar a tela.
function Invoke-Nativo($arquivo, $argumentos, $codificacao = "oem") {
    $encoding = if ($codificacao -eq "unicode") { [Text.Encoding]::Unicode } else { [Text.Encoding]::GetEncoding([Globalization.CultureInfo]::CurrentCulture.TextInfo.OEMCodePage) }
    $psi = New-Object Diagnostics.ProcessStartInfo $arquivo, $argumentos
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $psi.RedirectStandardOutput = $true; $psi.RedirectStandardError = $true
    $psi.StandardOutputEncoding = $encoding; $psi.StandardErrorEncoding = $encoding
    $proc = [Diagnostics.Process]::Start($psi)
    $erro = $proc.StandardError.ReadToEndAsync()
    $ultimoPercentual = -10
    while ($null -ne ($linha = $proc.StandardOutput.ReadLine())) {
        $linha = ($linha -replace "`0", "").Trim()
        if (-not $linha) { continue }
        if ($linha -match '(\d{1,3})(?:[.,]\d+)?\s?%') {
            $percentual = [int]$matches[1]
            if ($percentual -lt 100 -and $percentual -lt $ultimoPercentual + 10) { continue }
            $ultimoPercentual = $percentual
        }
        Write-Host "  $linha"
    }
    $proc.WaitForExit()
    $mensagemErro = $erro.Result.Trim()
    if ($mensagemErro) { Write-Host "  $mensagemErro" }
    $global:LASTEXITCODE = $proc.ExitCode
}

function Executar-VerificarSFC {
    Write-Secao "Verificando arquivos do sistema (SFC)"
    Write-Host "  Isso pode demorar alguns minutos..." -ForegroundColor Gray
    Write-Host ""
    Invoke-Nativo "sfc.exe" "/scannow" "unicode"
    if ($LASTEXITCODE -ne 0) { throw "Comando terminou com codigo $LASTEXITCODE. Consulte a saida para detalhes." }
    Write-Host ""
    Linha "="
}

function Executar-RepararDISM {
    Write-Secao "Reparando imagem do Windows (DISM)"
    Write-Host "  Isso pode demorar varios minutos e precisa de internet..." -ForegroundColor Gray
    Write-Host ""
    Invoke-Nativo "dism.exe" "/Online /Cleanup-Image /RestoreHealth"
    if ($LASTEXITCODE -ne 0) { throw "Comando terminou com codigo $LASTEXITCODE. Consulte a saida para detalhes." }
    Write-Host ""
    Linha "="
}

function Executar-VerificarDisco {
    Write-Secao "Verificando o disco (CHKDSK)"
    Write-Host "  Executando uma verificacao online (sem precisar reiniciar)..." -ForegroundColor Gray
    Write-Host ""
    Invoke-Nativo "chkdsk.exe" "$env:SystemDrive /scan"
    if ($LASTEXITCODE -ne 0) { throw "Comando terminou com codigo $LASTEXITCODE. Consulte a saida para detalhes." }
    Write-Host ""
    Linha "="
}

# Verificacao completa de corrupcao (como no Resources do Paragon): disco, arquivos do sistema,
# imagem do Windows e uma verificacao final. Uma etapa que falha nao impede as seguintes.
function Executar-VerificacaoCorrupcao {
    $etapas = @(
        @{ Nome = "[1/4] CHKDSK - verificando o disco"; Arq = "chkdsk.exe"; Args = "$env:SystemDrive /scan"; Cod = "oem" }
        @{ Nome = "[2/4] SFC - verificando arquivos do sistema"; Arq = "sfc.exe"; Args = "/scannow"; Cod = "unicode" }
        @{ Nome = "[3/4] DISM - reparando a imagem do Windows"; Arq = "dism.exe"; Args = "/Online /Cleanup-Image /RestoreHealth"; Cod = "oem" }
        @{ Nome = "[4/4] SFC - verificacao final"; Arq = "sfc.exe"; Args = "/scannow"; Cod = "unicode" }
    )
    foreach ($e in $etapas) {
        Write-Secao $e.Nome
        Invoke-Nativo $e.Arq $e.Args $e.Cod
        if ($LASTEXITCODE -eq 0) { Write-Resultado $true $e.Nome; $script:ContAplicados++ }
        else { Write-Resultado $false "$($e.Nome) (codigo $LASTEXITCODE)"; $script:ContFalhas++ }
        Write-Host ""
    }
    Linha "="
    Write-Host (Centralizar "Verificacao concluida: $script:ContAplicados etapa(s) ok, $script:ContFalhas com aviso") -ForegroundColor Gray
    Linha "="
}

function Executar-VerificarWindowsUpdate {
    Write-Secao "Verificando o Windows Update"
    Try {
        $wu = Get-Service wuauserv -ErrorAction Stop
        Write-Status "info" "Servico Windows Update: $($wu.Status) (StartType: $($wu.StartType))"
    } Catch {
        Write-Status "warn" "Nao foi possivel checar o servico do Windows Update."
    }
    Try {
        $ultimos = Get-HotFix -ErrorAction Stop | Sort-Object InstalledOn -Descending | Select-Object -First 5
        if ($ultimos) {
            Write-Host ""
            Write-Host "  Ultimas atualizacoes instaladas:" -ForegroundColor Cyan
            foreach ($u in $ultimos) {
                Write-Status "info" "$($u.HotFixID) - $($u.InstalledOn)"
            }
        }
    } Catch { }
    if (Test-PendingReboot) {
        Write-Status "warn" "Ha reinicializacao pendente relacionada a atualizacoes."
    } else {
        Write-Status "ok" "Nenhuma reinicializacao pendente."
    }
    Write-Host ""
    Linha "="
}

function Executar-LimparComponentesAntigos {
    Write-Secao "Limpando componentes antigos do Windows"
    Write-Host "  Removendo versoes antigas de componentes atualizados (WinSxS)..." -ForegroundColor Gray
    Write-Host ""
    DISM /Online /Cleanup-Image /StartComponentCleanup
    Write-Host ""
    Linha "="
}

function Executar-VerificarIntegridadeCompleta {
    Write-Secao "Verificacao completa de integridade (nao repara, so verifica)"
    Write-Host "  [1/2] SFC (verificacao)..." -ForegroundColor Gray
    sfc /verifyonly
    Write-Host ""
    Write-Host "  [2/2] DISM (verificacao)..." -ForegroundColor Gray
    DISM /Online /Cleanup-Image /ScanHealth
    Write-Host ""
    Write-Host "  Se algum problema foi encontrado acima, use as opcoes" -ForegroundColor Gray
    Write-Host "  [1] SFC ou [2] DISM (reparo completo) neste mesmo menu." -ForegroundColor Gray
    Write-Host ""
    Linha "="
}

function Menu-ManutencaoWindows {
    $continuarMenu = $true
    while ($continuarMenu) {
        Write-Secao "Manutencao do Windows"
        Write-Host "  [1] Verificar arquivos do sistema (SFC)" -ForegroundColor Cyan
        Write-Host "  [2] Reparar imagem do Windows (DISM)" -ForegroundColor Cyan
        Write-Host "  [3] Verificar disco (CHKDSK)" -ForegroundColor Cyan
        Write-Host "  [4] Verificar Windows Update" -ForegroundColor Cyan
        Write-Host "  [5] Limpar componentes antigos" -ForegroundColor Cyan
        Write-Host "  [6] Verificar integridade (rapido, so checa)" -ForegroundColor Cyan
        Write-Host "  [7] Voltar" -ForegroundColor Red
        Write-Host ""
        $opcao = Read-Host "  Escolha uma opcao"
        switch ($opcao) {
            "1" { Executar-VerificarSFC }
            "2" { Executar-RepararDISM }
            "3" { Executar-VerificarDisco }
            "4" { Executar-VerificarWindowsUpdate }
            "5" { Executar-LimparComponentesAntigos }
            "6" { Executar-VerificarIntegridadeCompleta }
            "7" { $continuarMenu = $false }
            default { Write-Host "  Opcao invalida." -ForegroundColor Red }
        }
        if ($continuarMenu) {
            Write-Host ""
            Read-Host "  Pressione ENTER para continuar"
        }
    }
}

# ---------------------------------------------------------------
# 12. Benchmark (teste rapido de desempenho, com comparacao antes/depois)
# ---------------------------------------------------------------
$script:BenchmarkPath = "$env:ProgramData\OtimizadorPC\Snapshots\benchmark.json"

# Mede a velocidade sequencial com o winsat (ferramenta oficial do Windows, ignora o cache de disco).
# Sem winsat, cai para o teste simples, cujo resultado de leitura e inflado pelo cache.
function Medir-VelocidadeDisco {
    $letra = $env:SystemDrive.TrimEnd(":")
    Try {
        $leitura = $null; $escrita = $null
        foreach ($modo in @("-read", "-write")) {
            $saida = & winsat.exe disk -seq $modo -drive $letra 2>&1 | Out-String
            if ($saida -match 'Sequential\s+64\.0\s+(Read|Write)\s+([\d.,]+)\s*MB/s') {
                $valor = [double]::Parse(($matches[2] -replace ',', '.'), [Globalization.CultureInfo]::InvariantCulture)
                if ($matches[1] -eq "Read") { $leitura = [Math]::Round($valor, 1) } else { $escrita = [Math]::Round($valor, 1) }
            }
        }
        if ($leitura -and $escrita) { return @{ Leitura = $leitura; Escrita = $escrita; Metodo = "winsat" } }
    } Catch { }
    $resultado = Medir-VelocidadeDiscoSimples
    $resultado.Metodo = "estimado"
    return $resultado
}

function Medir-VelocidadeDiscoSimples {
    $pasta = "$env:TEMP\otimizador_bench"
    New-Item -Path $pasta -ItemType Directory -Force | Out-Null
    $arquivo = Join-Path $pasta "teste.tmp"
    $tamanhoMB = 100
    $bloco = New-Object byte[] (1MB)
    (New-Object Random).NextBytes($bloco)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $fs = New-Object IO.FileStream($arquivo, [IO.FileMode]::Create, [IO.FileAccess]::Write, [IO.FileShare]::None, 1MB, [IO.FileOptions]::WriteThrough)
    for ($i = 0; $i -lt $tamanhoMB; $i++) { $fs.Write($bloco, 0, $bloco.Length) }
    $fs.Flush($true)
    $fs.Close()
    $sw.Stop()
    $escritaMBs = [Math]::Round($tamanhoMB / $sw.Elapsed.TotalSeconds, 1)

    [System.GC]::Collect()
    $sw2 = [System.Diagnostics.Stopwatch]::StartNew()
    $fs2 = [System.IO.File]::OpenRead($arquivo)
    $buffer = New-Object byte[] (1MB)
    while ($fs2.Read($buffer, 0, $buffer.Length) -gt 0) { }
    $fs2.Close()
    $sw2.Stop()
    $leituraMBs = [Math]::Round($tamanhoMB / $sw2.Elapsed.TotalSeconds, 1)

    Remove-Item -Path $pasta -Recurse -Force -ErrorAction SilentlyContinue
    return @{ Leitura = $leituraMBs; Escrita = $escritaMBs }
}

function Obter-TempoBoot {
    Try {
        $evt = Get-WinEvent -FilterHashtable @{ LogName = 'Microsoft-Windows-Diagnostics-Performance/Operational'; Id = 100 } -MaxEvents 1 -ErrorAction Stop
        $xml = [xml]$evt.ToXml()
        $ms = ($xml.Event.EventData.Data | Where-Object { $_.Name -eq 'BootTime' })."#text"
        if ($ms) { return [Math]::Round([double]$ms / 1000, 1) }
    } Catch { }
    return $null
}

function Executar-Benchmark {
    Write-Secao "Teste de desempenho (benchmark)"
    Write-Host "  Medindo CPU, RAM, disco, tempo de boot e latencia..." -ForegroundColor Gray
    Write-Host ""

    $cpuPercent = 0
    Try { $cpuPercent = [int](Get-CimInstance Win32_Processor | Select-Object -First 1).LoadPercentage } Catch { }
    $ramPercent = 0
    Try {
        $os = Get-CimInstance Win32_OperatingSystem
        $ramPercent = [Math]::Round((($os.TotalVisibleMemorySize - $os.FreePhysicalMemory) / $os.TotalVisibleMemorySize) * 100)
    } Catch { }
    $disco = Medir-VelocidadeDisco
    $bootSeg = Obter-TempoBoot
    $latencia = Medir-LatenciaDPC 5
    $temperatura = Obter-Temperatura
    $discoLivrePercent = $null
    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $vol = Get-Volume -DriveLetter $letra -ErrorAction Stop
        $discoLivrePercent = [Math]::Round(($vol.SizeRemaining / $vol.Size) * 100)
    } Catch { }

    # Uso atual, nao nota: a barra do Health Score pintaria 90% de uso de verde
    Write-Host "  CPU E MEMORIA" -ForegroundColor Cyan
    Write-Status "info" "CPU em uso agora: $cpuPercent%"
    Write-Status "info" "RAM em uso agora: $ramPercent%"
    Write-Host ""
    Write-Host "  DISCO" -ForegroundColor Cyan
    if ($disco.Metodo -ne "winsat") { Write-Status "warn" "winsat indisponivel: leitura estimada (pode estar acima do real por causa do cache)" }
    Write-Status "info" "Leitura:  $($disco.Leitura) MB/s"
    Write-Status "info" "Escrita:  $($disco.Escrita) MB/s"
    if ($null -ne $discoLivrePercent) { Write-Status "info" "Espaco usado: $(100 - $discoLivrePercent)%" }
    Write-Host ""
    if ($bootSeg) {
        Write-Host "  BOOT" -ForegroundColor Cyan
        Write-Status "info" "Ultima inicializacao: $bootSeg s"
    }
    if ($latencia) {
        Write-Host ""
        Write-Host "  LATENCIA (DPC / INTERRUPCOES)" -ForegroundColor Cyan
        Write-Status $latencia.Nivel "DPC: $($latencia.DPC)% da CPU"
        Write-Status $latencia.Nivel "Interrupcoes: $($latencia.Interrupcao)% da CPU ($($latencia.PorSegundo)/s)"
    }
    if ($null -ne $temperatura) {
        $nivelTemp = if ($temperatura -ge 85) { "bad" } elseif ($temperatura -ge 70) { "warn" } else { "ok" }
        Write-Status $nivelTemp "Temperatura: $temperatura C (sensor ACPI)"
    }

    $resultadoAtual = [PSCustomObject]@{
        Data          = (Get-Date).ToString("dd/MM/yyyy HH:mm:ss")
        CPU           = $cpuPercent
        RAM           = $ramPercent
        DiscoLeitura  = $disco.Leitura
        DiscoEscrita  = $disco.Escrita
        DiscoUsado    = if ($null -ne $discoLivrePercent) { 100 - $discoLivrePercent } else { $null }
        BootSegundos  = $bootSeg
        DPC           = if ($latencia) { $latencia.DPC } else { $null }
        Interrupcao   = if ($latencia) { $latencia.Interrupcao } else { $null }
        Temperatura   = $temperatura
    }

    if (Test-Path $script:BenchmarkPath) {
        Try {
            $anterior = Get-Content -Path $script:BenchmarkPath -Raw | ConvertFrom-Json

            $bootAntesTxt = "N/D"
            if ($anterior.BootSegundos) { $bootAntesTxt = "$($anterior.BootSegundos)s" }
            $bootDepoisTxt = "N/D"
            if ($bootSeg) { $bootDepoisTxt = "${bootSeg}s" }

            Write-Host ""
            Linha "="
            Write-Host (Centralizar "ANTES -> DEPOIS (desde o ultimo benchmark)") -ForegroundColor Yellow
            Linha "="
            Write-Host ("  Data anterior : {0}" -f $anterior.Data) -ForegroundColor DarkGray
            Write-Host ("  Boot          : {0} -> {1}" -f $bootAntesTxt, $bootDepoisTxt) -ForegroundColor Gray
            Write-Host ("  RAM em uso    : {0}% -> {1}%" -f $anterior.RAM, $ramPercent) -ForegroundColor Gray
            Write-Host ("  Disco usado   : {0}% -> {1}%" -f $anterior.DiscoUsado, $resultadoAtual.DiscoUsado) -ForegroundColor Gray
            Write-Host ("  Leitura disco : {0} MB/s -> {1} MB/s" -f $anterior.DiscoLeitura, $disco.Leitura) -ForegroundColor Gray
            Write-Host ("  Escrita disco : {0} MB/s -> {1} MB/s" -f $anterior.DiscoEscrita, $disco.Escrita) -ForegroundColor Gray
            if ($null -ne $anterior.DPC -and $latencia) {
                Write-Host ("  Latencia DPC  : {0}% -> {1}%" -f $anterior.DPC, $latencia.DPC) -ForegroundColor Gray
                Write-Host ("  Interrupcoes  : {0}% -> {1}%" -f $anterior.Interrupcao, $latencia.Interrupcao) -ForegroundColor Gray
            }
            if ($null -ne $anterior.Temperatura -and $null -ne $temperatura) {
                Write-Host ("  Temperatura   : {0} C -> {1} C" -f $anterior.Temperatura, $temperatura) -ForegroundColor Gray
            }
        } Catch { }
    } else {
        Write-Host ""
        Write-Host "  Este e o primeiro benchmark salvo. Rode novamente depois de" -ForegroundColor DarkGray
        Write-Host "  otimizar o PC para ver a comparacao antes -> depois." -ForegroundColor DarkGray
    }

    New-Item -Path (Split-Path $script:BenchmarkPath) -ItemType Directory -Force | Out-Null
    $resultadoAtual | ConvertTo-Json | Out-File -FilePath $script:BenchmarkPath -Encoding UTF8 -Force

    Write-Host ""
    Write-Host "  Obs: isso NAO garante ganho de FPS - mede CPU/RAM/disco, boot e latencia." -ForegroundColor DarkGray
    Write-Host ""
    Linha "="
}

# ---------------------------------------------------------------
# 13. Menu principal (painel estilo dashboard: sistema + opcoes)
# ---------------------------------------------------------------

# Cor associada a cada nivel (mesmo padrao usado em Write-Status)
function Cor-Nivel($nivel) {
    switch ($nivel) {
        "ok"   { return "Green" }
        "warn" { return "Yellow" }
        "bad"  { return "Red" }
        default { return "White" }
    }
}

# Colhido uma unica vez (no inicio da sessao) para o menu abrir instantaneamente
$script:InfoResumo = $null

function Obter-InfoResumo {
    $info = [ordered]@{
        OS         = "Nao disponivel"
        CPU        = "Nao disponivel"
        RAM        = "Nao disponivel"
        GPU        = "Nao disponivel"
        Disco      = "Nao disponivel"
        DiscoNivel = "info"
    }
    Try {
        $os = Get-CimInstance Win32_OperatingSystem
        $info.OS = ($os.Caption -replace "Microsoft ", "").Trim()
    } Catch { }
    Try {
        $cpu = Get-CimInstance Win32_Processor | Select-Object -First 1
        $info.CPU = $cpu.Name.Trim()
    } Catch { }
    Try {
        $os2 = Get-CimInstance Win32_OperatingSystem
        $totalGB = [Math]::Round($os2.TotalVisibleMemorySize / 1MB, 0)
        $info.RAM = "$totalGB GB"
    } Catch { }
    Try {
        $gpu = Get-CimInstance Win32_VideoController | Select-Object -First 1
        $info.GPU = $gpu.Name.Trim()
    } Catch { }
    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $vol = Get-Volume -DriveLetter $letra -ErrorAction Stop
        $livrePercent = [Math]::Round(($vol.SizeRemaining / $vol.Size) * 100)
        $info.Disco = "$livrePercent% livre de $(Format-Bytes $vol.Size)"
        $info.DiscoNivel = if ($livrePercent -le 10) { "bad" } elseif ($livrePercent -le 20) { "warn" } else { "ok" }
    } Catch { }
    return $info
}

$script:DashEsqLarg = 27
$script:DashDirLarg = 55

# Corta o texto (com "...") se ele nao couber na largura da coluna,
# para o painel nunca quebrar mesmo com nomes de CPU/GPU muito longos
function Encurtar($texto, $largura) {
    if ($null -eq $texto) { $texto = "" }
    if ($texto.Length -gt $largura) {
        if ($largura -le 3) { return $texto.Substring(0, $largura) }
        return $texto.Substring(0, $largura - 3) + "..."
    }
    return $texto
}

function Dash-Topo { Write-Host ("  " + "┌" + ("─" * $script:DashEsqLarg) + "┬" + ("─" * $script:DashDirLarg) + "┐") -ForegroundColor DarkCyan }
function Dash-Sep  { Write-Host ("  " + "├" + ("─" * $script:DashEsqLarg) + "┼" + ("─" * $script:DashDirLarg) + "┤") -ForegroundColor DarkCyan }
function Dash-Base { Write-Host ("  " + "└" + ("─" * $script:DashEsqLarg) + "┴" + ("─" * $script:DashDirLarg) + "┘") -ForegroundColor DarkCyan }

# Uma linha do painel, com uma celula na coluna esquerda (sistema) e
# uma na coluna direita (opcoes), cada uma com sua propria cor
function Dash-Linha($esqTexto, $esqCor, $dirTexto, $dirCor) {
    $e = Encurtar $esqTexto $script:DashEsqLarg
    $d = Encurtar $dirTexto $script:DashDirLarg
    $padE = $script:DashEsqLarg - $e.Length
    $padD = $script:DashDirLarg - $d.Length
    Write-Host "  │" -NoNewline -ForegroundColor DarkCyan
    Write-Host $e -NoNewline -ForegroundColor $esqCor
    if ($padE -gt 0) { Write-Host (" " * $padE) -NoNewline }
    Write-Host "│" -NoNewline -ForegroundColor DarkCyan
    Write-Host $d -NoNewline -ForegroundColor $dirCor
    if ($padD -gt 0) { Write-Host (" " * $padD) -NoNewline }
    Write-Host "│" -ForegroundColor DarkCyan
}

function Mostrar-Menu {
    Write-Banner

    if (-not $script:InfoResumo) { $script:InfoResumo = Obter-InfoResumo }
    $info = $script:InfoResumo

    if ($script:UltimoScore) {
        $ScoreTexto = "$($script:UltimoScore.Geral)/100"
        if ($script:UltimoScore.Geral -ge 80) { $ScoreCor = "Green" }
        elseif ($script:UltimoScore.Geral -ge 50) { $ScoreCor = "Yellow" }
        else { $ScoreCor = "Red" }
    } else {
        $ScoreTexto = "use [0] p/ calcular"
        $ScoreCor = "DarkGray"
    }

    $esquerda = @(
        @{ T = "";                              C = "White" }
        @{ T = " OS";                            C = "DarkGray" }
        @{ T = " $($info.OS)";                   C = "Cyan" }
        @{ T = "";                               C = "White" }
        @{ T = " CPU";                           C = "DarkGray" }
        @{ T = " $($info.CPU)";                  C = "Cyan" }
        @{ T = "";                               C = "White" }
        @{ T = " RAM";                           C = "DarkGray" }
        @{ T = " $($info.RAM)";                  C = "Cyan" }
        @{ T = "";                               C = "White" }
        @{ T = " GPU";                           C = "DarkGray" }
        @{ T = " $($info.GPU)";                  C = "Cyan" }
        @{ T = "";                               C = "White" }
        @{ T = " DISCO $($env:SystemDrive)";     C = "DarkGray" }
        @{ T = " $($info.Disco)";                C = (Cor-Nivel $info.DiscoNivel) }
        @{ T = "";                               C = "White" }
        @{ T = "";                               C = "White" }
        @{ T = " STATUS";                        C = "DarkGray" }
        @{ T = " * ADMINISTRADOR";               C = "Green" }
        @{ T = "   Privilegios elevados";        C = "DarkGray" }
        @{ T = "";                               C = "White" }
        @{ T = " PC HEALTH SCORE";               C = "DarkGray" }
        @{ T = " $ScoreTexto";                   C = $ScoreCor }
        @{ T = "";                               C = "White" }
    )

    $direita = @(
        @{ T = " [0] ANALISAR PC";                                      C = "White" }
        @{ T = "     Health Score + problemas encontrados";             C = "DarkGray" }
        @{ T = " [1] OTIMIZACAO INTELIGENTE";                           C = "White" }
        @{ T = "     O programa analisa e decide o que aplicar";        C = "DarkGray" }
        @{ T = "";                                                      C = "White" }
        @{ T = " [2] VERSAO PADRAO";                                    C = "Cyan" }
        @{ T = "     Limpeza e ajustes para uso diario";                C = "DarkGray" }
        @{ T = " [3] VERSAO AVANCADA";                                  C = "Magenta" }
        @{ T = "     Desempenho maximo para jogos";                     C = "DarkGray" }
        @{ T = " [4] DEBLOAT";                                          C = "Blue" }
        @{ T = "     Remove apps e servicos (pergunta ou tudo de vez)"; C = "DarkGray" }
        @{ T = "";                                                      C = "White" }
        @{ T = " [5] MANUTENCAO DO WINDOWS";                            C = "Cyan" }
        @{ T = "     SFC, DISM, CHKDSK, Windows Update";                C = "DarkGray" }
        @{ T = " [6] BENCHMARK";                                        C = "Yellow" }
        @{ T = "     Mede CPU/RAM/disco - compara com o ultimo teste";  C = "DarkGray" }
        @{ T = "";                                                      C = "White" }
        @{ T = " [7] REVERTER ULTIMA OTIMIZACAO";                       C = "DarkCyan" }
        @{ T = "     Desfaz a acao mais recente";                       C = "DarkGray" }
        @{ T = " [8] GERENCIAR INICIALIZACAO";                          C = "Green" }
        @{ T = "     Escolher o que abre com o Windows";                C = "DarkGray" }
        @{ T = "";                                                      C = "White" }
        @{ T = " [9] SAIR";                                             C = "Red" }
        @{ T = "";                                                      C = "White" }
    )

    Dash-Topo
    Dash-Linha " SISTEMA" "Yellow" " OTIMIZACOES DISPONIVEIS" "Yellow"
    Dash-Sep

    $totalLinhas = [Math]::Max($esquerda.Count, $direita.Count)
    for ($i = 0; $i -lt $totalLinhas; $i++) {
        $e = if ($i -lt $esquerda.Count) { $esquerda[$i] } else { @{ T = ""; C = "White" } }
        $d = if ($i -lt $direita.Count)  { $direita[$i]  } else { @{ T = ""; C = "White" } }
        Dash-Linha $e.T $e.C $d.T $d.C
    }

    Dash-Base
    Write-Host ""
    Write-Host "  root@otimizador" -NoNewline -ForegroundColor Green
    Write-Host ":" -NoNewline -ForegroundColor DarkGray
    Write-Host "~$ " -NoNewline -ForegroundColor Green
    $escolha = Read-Host "escolha uma opcao (0-9)"
    return $escolha
}

function Pausar-Menu {
    Write-Host ""
    Read-Host "  Pressione ENTER para voltar ao menu"
}

$script:UltimoScore = $null
if (-not $UiMode) { Set-Aparencia }

try {
    Proteger-PastaDados
    if ($Operation) {
        switch ($Operation) {
            "analisar" { Analisar-PC }
            "inteligente" { Otimizar-Inteligente }
            "padrao" { Otimizar-Padrao }
            "gamer" { Otimizar-Gamer }
            "gamerservicos" { Ativar-ModoSemServicos; Otimizar-Gamer }
            "debloat" { Otimizar-Debloat }
            "reverter" { Reverter-UltimaOtimizacao }
            "sfc" { Executar-VerificarSFC }
            "dism" { Executar-RepararDISM }
            "chkdsk" { Executar-VerificarDisco }
            "update" { Executar-VerificarWindowsUpdate }
            "reparar" { Executar-RepararConfiguracoes }
            "benchmark" { Executar-Benchmark }
            "corrupcao" { Executar-VerificacaoCorrupcao }
        }
        if ($script:ContFalhas -gt 0) { exit 1 }
        exit 0
    }
    $continuar = $true
    while ($continuar) {
        $opcao = Mostrar-Menu
        switch ($opcao) {
            "0" { Analisar-PC;               Pausar-Menu }
            "1" { Otimizar-Inteligente;      Pausar-Menu }
            "2" { Otimizar-Padrao;           Pausar-Menu }
            "3" { Otimizar-Gamer;            Pausar-Menu }
            "4" { Otimizar-Debloat;          Pausar-Menu }
            "5" { Menu-ManutencaoWindows;    Pausar-Menu }
            "6" { Executar-Benchmark;        Pausar-Menu }
            "7" { Reverter-UltimaOtimizacao; Pausar-Menu }
            "8" { Gerenciar-Inicializacao;   Pausar-Menu }
            "9" { $continuar = $false }
            default {
                Write-Host ""
                Write-Host "  Opcao invalida." -ForegroundColor Red
                Start-Sleep -Seconds 1
            }
        }
    }
    Write-Host ""
    Write-Host "  Ate a proxima!" -ForegroundColor Cyan
}
catch {
    Write-Host ""
    Write-Host "  Ocorreu um erro durante a execucao:" -ForegroundColor Red
    Write-Host "  $($_.Exception.Message)" -ForegroundColor Red
    if ($Operation) { exit 1 }
}
finally {
    if (-not $Operation) {
        Write-Host ""
        Read-Host "  Pressione ENTER para sair"
    }
}
