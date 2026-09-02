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

# ---------------------------------------------------------------
# 0. Auto-elevacao (garante que o script rode como Administrador)
# ---------------------------------------------------------------
$currentPrincipal = New-Object Security.Principal.WindowsPrincipal([Security.Principal.WindowsIdentity]::GetCurrent())
if (-not $currentPrincipal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
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

$ErrorActionPreference = "SilentlyContinue"

# ---------------------------------------------------------------
# ESTADO GLOBAL - usado pelo sistema de Reverter Ultima Otimizacao
# ---------------------------------------------------------------
$script:SnapshotDir  = "$env:ProgramData\OtimizadorPC\Snapshots"
$script:SnapshotPath = Join-Path $script:SnapshotDir "ultima_otimizacao.json"
$script:SnapshotAtual = @()
$script:SnapshotNome  = ""
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
    $resp = Read-Host "  >> $pergunta (S/N)"
    return ($resp -match '^[Ss]')
}

# Bolinha colorida para linhas de diagnostico (ok / warn / bad / info)
function Write-Status($nivel, $texto) {
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

# Comeca a registrar uma nova sessao de otimizacao (zera contadores e o registro anterior)
function Iniciar-Snapshot($nome) {
    $script:SnapshotAtual = @()
    $script:SnapshotNome  = $nome
    $script:ContAplicados = 0
    $script:ContPulados   = 0
    $script:ContFalhas    = 0
}

# Salva em disco tudo que foi registrado nesta sessao (sobrescreve o snapshot anterior:
# so guardamos a ULTIMA otimizacao, que e o que o Reverter usa)
function Salvar-Snapshot {
    Try {
        New-Item -Path $script:SnapshotDir -ItemType Directory -Force | Out-Null
        $objeto = [PSCustomObject]@{
            Nome  = $script:SnapshotNome
            Data  = (Get-Date).ToString("dd/MM/yyyy HH:mm:ss")
            Itens = $script:SnapshotAtual
        }
        $objeto | ConvertTo-Json -Depth 6 | Out-File -FilePath $script:SnapshotPath -Encoding UTF8 -Force
    } Catch { }
}

# Guarda o valor ATUAL de uma chave/valor de registro antes de ele ser alterado
function Capturar-Registro($caminho, $nome) {
    $existia = $false
    $valorAnterior = $null
    Try {
        if (Test-Path $caminho) {
            $prop = Get-ItemProperty -Path $caminho -Name $nome -ErrorAction Stop
            if ($prop.PSObject.Properties.Name -contains $nome) {
                $existia = $true
                $valorAnterior = $prop.$nome
            }
        }
    } Catch { }
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo          = "Registro"
        Caminho       = $caminho
        Nome          = $nome
        Existia       = $existia
        ValorAnterior = $valorAnterior
    }
}

# Guarda o tipo de inicializacao e o status ATUAL de um servico antes de ele ser alterado
function Capturar-Servico($nomeServico) {
    Try {
        $svc = Get-Service -Name $nomeServico -ErrorAction Stop
        $script:SnapshotAtual += [PSCustomObject]@{
            Tipo                = "Servico"
            Nome                = $nomeServico
            StartupTypeAnterior = $svc.StartType.ToString()
            StatusAnterior      = $svc.Status.ToString()
        }
    } Catch { }
}

# Guarda qual e o plano de energia ATIVO antes de trocar de plano
function Capturar-PlanoEnergia {
    $atual = powercfg /getactivescheme
    if ($atual -match "([0-9a-fA-F-]{36})") {
        $script:SnapshotAtual += [PSCustomObject]@{
            Tipo         = "PlanoEnergia"
            GuidAnterior = $matches[1]
        }
    }
}

# Guarda que uma tarefa agendada foi desativada (para poder reativar depois)
function Capturar-TarefaAgendada($nomeTarefa) {
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo = "TarefaAgendada"
        Nome = $nomeTarefa
    }
}

# Guarda o caminho do instalador do OneDrive (para poder reinstalar depois)
function Capturar-OneDrive($caminhoInstalador) {
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo    = "OneDrive"
        Caminho = $caminhoInstalador
    }
}

# Registra uma alteracao que NAO pode ser desfeita automaticamente (ex: apps removidos)
function Registrar-Irreversivel($descricao) {
    $script:SnapshotAtual += [PSCustomObject]@{
        Tipo      = "Irreversivel"
        Descricao = $descricao
    }
}

# Executa uma lista de etapas mostrando barra de progresso + contador + status OK/FALHOU
function Executar-Etapas($atividade, $etapas) {
    $total = $etapas.Count
    $i = 0
    foreach ($etapa in $etapas) {
        $i++
        $percent = [Math]::Round(($i / $total) * 100)
        Write-Progress -Activity $atividade -Status "[$i/$total] $($etapa.Nome)" -PercentComplete $percent
        $sucesso = $true
        Try {
            & $etapa.Acao | Out-Null
        } Catch {
            $sucesso = $false
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
    if (-not $modoRapido) {
        $aplicar = Confirmar $pergunta
    }
    if ($aplicar) {
        $sucesso = $true
        Try {
            & $acao | Out-Null
        } Catch {
            $sucesso = $false
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
function Criar-PontoDeRestauracao {
    Write-Secao "Criando ponto de restauracao"
    $etapas = @(
        @{ Nome = "Habilitando restauracao do sistema no disco"; Acao = { Enable-ComputerRestore -Drive "$env:SystemDrive\" } }
        @{ Nome = "Criando ponto de restauracao"; Acao = { Checkpoint-Computer -Description "Antes do Otimizador de PC" -RestorePointType "MODIFY_SETTINGS" } }
    )
    Executar-Etapas "Ponto de restauracao" $etapas
}

# ---------------------------------------------------------------
# 2. Limpeza de arquivos temporarios (usada nas duas versoes)
# ---------------------------------------------------------------
function Limpar-Temporarios {
    Write-Secao "Limpando arquivos temporarios e cache"
    $etapas = @(
        @{ Nome = "Pasta TEMP do usuario";              Acao = { Remove-Item "$env:TEMP\*" -Recurse -Force } }
        @{ Nome = "Pasta TEMP do Windows";               Acao = { Remove-Item "$env:SystemRoot\Temp\*" -Recurse -Force } }
        @{ Nome = "Cache de Prefetch";                   Acao = { Remove-Item "$env:SystemRoot\Prefetch\*" -Recurse -Force } }
        @{ Nome = "Lixeira";                              Acao = { Clear-RecycleBin -Force } }
        @{ Nome = "Cache do Windows Update";             Acao = {
                Stop-Service wuauserv -Force
                Remove-Item "$env:SystemRoot\SoftwareDistribution\Download\*" -Recurse -Force
                Start-Service wuauserv
            } }
        @{ Nome = "Cache de miniaturas";                 Acao = { Remove-Item "$env:LOCALAPPDATA\Microsoft\Windows\Explorer\thumbcache_*.db" -Force } }
        @{ Nome = "Cache do Chrome";                     Acao = { Remove-Item "$env:LOCALAPPDATA\Google\Chrome\User Data\Default\Cache" -Recurse -Force } }
        @{ Nome = "Cache do Edge";                       Acao = { Remove-Item "$env:LOCALAPPDATA\Microsoft\Edge\User Data\Default\Cache" -Recurse -Force } }
        @{ Nome = "Cache do Firefox";                    Acao = { Remove-Item "$env:APPDATA\Mozilla\Firefox\Profiles\*\cache2" -Recurse -Force } }
    )
    Executar-Etapas "Limpeza de temporarios" $etapas
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
        @{ Nome = "Plano de energia 'Alto Desempenho'"; Acao = {
                Capturar-PlanoEnergia
                powercfg /setactive SCHEME_MIN
            } }
        @{ Nome = "Limpando fila de impressao";          Acao = {
                Stop-Service Spooler -Force
                Remove-Item "$env:SystemRoot\System32\spool\PRINTERS\*" -Force
                Start-Service Spooler
            } }
        @{ Nome = "Otimizando/TRIM das unidades de disco"; Acao = {
                Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
                    Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim
                }
            } }
        @{ Nome = "Limpando cache DNS";                  Acao = { ipconfig /flushdns } }
        @{ Nome = "Executando limpeza de disco (cleanmgr)"; Acao = { Start-Process cleanmgr.exe -ArgumentList "/sagerun:1" -WindowStyle Hidden } }
    )
    Executar-Etapas "Otimizacao Padrao" $etapas
    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "OTIMIZACAO PADRAO CONCLUIDA!") -ForegroundColor Green
    Write-Host (Centralizar "Seu PC deve estar mais leve e rapido.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray
    Linha "="
}

# ---------------------------------------------------------------
# 4. Otimizacoes avancadas (Versao Gamer)
# ---------------------------------------------------------------
function Otimizar-Gamer {
    Iniciar-Snapshot "Versao Gamer"
    Criar-PontoDeRestauracao
    Limpar-Temporarios

    Write-Secao "Aplicando otimizacoes de desempenho maximo para jogos"
    $etapas = @(
        @{ Nome = "Plano de energia 'Desempenho Maximo'"; Acao = {
                Capturar-PlanoEnergia
                $ultimatePlan = powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
                if ($ultimatePlan -match "([0-9a-fA-F-]{36})") {
                    powercfg /setactive $matches[1]
                } else {
                    powercfg /setactive SCHEME_MIN
                }
            } }
        @{ Nome = "Priorizando CPU para o jogo em foco";  Acao = {
                Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation"
                Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\PriorityControl" "Win32PrioritySeparation" 38 -Type DWord
            } }
        @{ Nome = "Reduzindo latencia de rede/multimidia"; Acao = {
                $multimediaPath = "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile"
                New-Item -Path $multimediaPath -Force | Out-Null
                Capturar-Registro $multimediaPath "SystemResponsiveness"
                Set-ItemProperty $multimediaPath "SystemResponsiveness" 0 -Type DWord

                $gamesPath = "$multimediaPath\Tasks\Games"
                New-Item -Path $gamesPath -Force | Out-Null
                Capturar-Registro $gamesPath "GPU Priority"
                Set-ItemProperty $gamesPath "GPU Priority" 8 -Type DWord
                Capturar-Registro $gamesPath "Priority"
                Set-ItemProperty $gamesPath "Priority" 6 -Type DWord
                Capturar-Registro $gamesPath "Scheduling Category"
                Set-ItemProperty $gamesPath "Scheduling Category" "High" -Type String
            } }
        @{ Nome = "Desativando limitacao de rede (Throttling)"; Acao = {
                Capturar-Registro "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex"
                Set-ItemProperty "HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion\Multimedia\SystemProfile" "NetworkThrottlingIndex" 0xffffffff -Type DWord
            } }
        @{ Nome = "Desativando Xbox Game Bar / Game DVR"; Acao = {
                Capturar-Registro "HKCU:\System\GameConfigStore" "GameDVR_Enabled"
                Set-ItemProperty "HKCU:\System\GameConfigStore" "GameDVR_Enabled" 0 -Type DWord
                New-Item -Path "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" -Force | Out-Null
                Capturar-Registro "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" "AllowGameDVR"
                Set-ItemProperty "HKLM:\SOFTWARE\Policies\Microsoft\Windows\GameDVR" "AllowGameDVR" 0 -Type DWord
            } }
        @{ Nome = "Ativando Modo de Jogo do Windows";     Acao = {
                New-Item -Path "HKCU:\Software\Microsoft\GameBar" -Force | Out-Null
                Capturar-Registro "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled"
                Set-ItemProperty "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled" 1 -Type DWord
            } }
        @{ Nome = "Habilitando GPU Scheduling por hardware"; Acao = {
                Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode"
                Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\GraphicsDrivers" "HwSchMode" 2 -Type DWord
            } }
        @{ Nome = "Ajustando efeitos visuais p/ desempenho"; Acao = {
                Capturar-Registro "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" "VisualFXSetting"
                Set-ItemProperty "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects" "VisualFXSetting" 2 -Type DWord
            } }
        @{ Nome = "Ajustando SysMain e Windows Search";   Acao = {
                Capturar-Servico "SysMain"
                Set-Service "SysMain" -StartupType Manual
                Capturar-Servico "WSearch"
                Set-Service "WSearch" -StartupType Manual
            } }
        @{ Nome = "Limpando cache DNS";                   Acao = { ipconfig /flushdns } }
        @{ Nome = "Otimizando unidades de disco";         Acao = {
                Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
                    Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim
                }
            } }
    )
    Executar-Etapas "Otimizacao Gamer" $etapas
    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "OTIMIZACAO GAMER CONCLUIDA!") -ForegroundColor Green
    Write-Host (Centralizar "Reinicie o PC para aplicar todas as mudancas.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray
    Linha "="
}

# ---------------------------------------------------------------
# 5. Debloat (pergunta Sim/Nao para cada etapa, ou tudo de uma vez no modo rapido)
# ---------------------------------------------------------------
function Otimizar-Debloat {
    Iniciar-Snapshot "Debloat"
    Criar-PontoDeRestauracao

    Write-Secao "Debloat do Windows"
    Write-Host "  Para cada item abaixo, digite S para aplicar ou N (ou ENTER) para pular." -ForegroundColor Gray
    Write-Host "  Nada e alterado sem sua confirmacao." -ForegroundColor Gray
    Write-Host ""
    $modoEscolha = Read-Host "  Prefere aplicar TUDO de uma vez, sem perguntar item por item? (S/N)"
    $modoRapido = ($modoEscolha -match '^[Ss]')
    if ($modoRapido) {
        Write-Host ""
        Write-Host "  Modo rapido ativado: todas as etapas abaixo serao aplicadas automaticamente." -ForegroundColor Yellow
    }

    # 1. Telemetria e diagnostico
    Executar-Se-Confirmado "Desativar servicos de telemetria e diagnostico (DiagTrack, WerSvc, PcaSvc, etc)?" `
        "Telemetria e diagnostico desativados" `
        {
            $services = @("DiagTrack", "dmwappushservice", "diagnosticshub.standardcollector.service", "WerSvc", "PcaSvc")
            foreach ($service in $services) {
                if (Get-Service $service -ErrorAction SilentlyContinue) {
                    Capturar-Servico $service
                    Stop-Service $service -Force
                    Set-Service $service -StartupType Disabled
                }
            }
        } $modoRapido

    # 2. Cortana
    Executar-Se-Confirmado "Remover componentes relacionados a Cortana?" `
        "Cortana removida" `
        {
            Registrar-Irreversivel "Cortana (app removido - reinstale pela Microsoft Store se precisar)"
            Get-AppxPackage -AllUsers -Name "*Microsoft.549981C3F5F10*" | Remove-AppxPackage -AllUsers
        } $modoRapido

    # 3. Widgets / Web Experience
    Executar-Se-Confirmado "Remover Widgets / Windows Web Experience?" `
        "Widgets removidos" `
        {
            Registrar-Irreversivel "Widgets/Web Experience (app removido - reinstale pela Microsoft Store se precisar)"
            Get-AppxPackage -AllUsers | Where-Object {
                $_.Name -like "*WebExperience*" -or $_.Name -like "*WindowsWidgets*"
            } | Remove-AppxPackage -AllUsers
        } $modoRapido

    # 4. OneDrive
    Executar-Se-Confirmado "Remover o OneDrive?" `
        "OneDrive removido" `
        {
            $oneDrivePaths = @(
                "$env:SystemRoot\System32\OneDriveSetup.exe",
                "$env:SystemRoot\SysWOW64\OneDriveSetup.exe"
            )
            $instaladorEncontrado = $oneDrivePaths | Where-Object { Test-Path $_ } | Select-Object -First 1
            if ($instaladorEncontrado) {
                Capturar-OneDrive $instaladorEncontrado
            } else {
                Registrar-Irreversivel "OneDrive (instalador nao encontrado para reinstalacao automatica)"
            }
            Stop-Process -Name "OneDrive" -Force
            foreach ($path in $oneDrivePaths) {
                if (Test-Path $path) {
                    Start-Process $path -ArgumentList "/uninstall" -Wait
                }
            }
        } $modoRapido

    # 5. Xbox - mantido por padrao (apenas informativo, sem alteracao)
    Write-Host ""
    Write-Host "   [ i] Xbox/Game Bar: mantido (nao e alterado nesta secao)." -ForegroundColor DarkGray

    # 6. Impressao
    Executar-Se-Confirmado "Desativar o servico de Impressao (Spooler)? So faca isso se NAO usa impressora." `
        "Servico de impressao desativado" `
        {
            Capturar-Servico "Spooler"
            Stop-Service "Spooler" -Force
            Set-Service "Spooler" -StartupType Disabled
        } $modoRapido

    # 7. Bluetooth
    Executar-Se-Confirmado "Desativar o servico de Bluetooth? So faca isso se NAO usa Bluetooth." `
        "Bluetooth desativado" `
        {
            Capturar-Servico "bthserv"
            Stop-Service "bthserv" -Force
            Set-Service "bthserv" -StartupType Disabled
        } $modoRapido

    # 8. Fax
    Executar-Se-Confirmado "Desativar o servico de Fax?" `
        "Servico de Fax desativado" `
        {
            Capturar-Servico "Fax"
            Stop-Service "Fax" -Force
            Set-Service "Fax" -StartupType Disabled
        } $modoRapido

    # 9. Remote Registry
    Executar-Se-Confirmado "Desativar o servico de Registro Remoto (Remote Registry)?" `
        "Remote Registry desativado" `
        {
            Capturar-Servico "RemoteRegistry"
            Stop-Service "RemoteRegistry" -Force
            Set-Service "RemoteRegistry" -StartupType Disabled
        } $modoRapido

    # 10. Remote Assistance
    Executar-Se-Confirmado "Desativar a Assistencia Remota (Remote Assistance)?" `
        "Assistencia Remota desativada" `
        {
            Capturar-Registro "HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance" "fAllowToGetHelp"
            Set-ItemProperty "HKLM:\SYSTEM\CurrentControlSet\Control\Remote Assistance" "fAllowToGetHelp" -Type DWord -Value 0
        } $modoRapido

    # 11. Mapas
    Executar-Se-Confirmado "Desativar o servico de Mapas (Maps Broker)?" `
        "Servico de Mapas desativado" `
        {
            Capturar-Servico "MapsBroker"
            Stop-Service "MapsBroker" -Force
            Set-Service "MapsBroker" -StartupType Disabled
        } $modoRapido

    # 12. Servicos de diagnostico adicionais
    Executar-Se-Confirmado "Desativar servicos adicionais de diagnostico (WdiServiceHost, WdiSystemHost, DPS)?" `
        "Servicos adicionais de diagnostico desativados" `
        {
            $diagnosticServices = @("WdiServiceHost", "WdiSystemHost", "DPS")
            foreach ($service in $diagnosticServices) {
                if (Get-Service $service -ErrorAction SilentlyContinue) {
                    Capturar-Servico $service
                    Stop-Service $service -Force
                    Set-Service $service -StartupType Disabled
                }
            }
        } $modoRapido

    # 13. Tarefas agendadas de telemetria
    Executar-Se-Confirmado "Desativar tarefas agendadas de telemetria/CEIP?" `
        "Tarefas de telemetria desativadas" `
        {
            $tasks = @(
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
            foreach ($task in $tasks) {
                Capturar-TarefaAgendada $task
                schtasks.exe /Change /TN $task /Disable | Out-Null
            }
        } $modoRapido

    # 14. Apps provisionados desnecessarios
    Executar-Se-Confirmado "Remover apps pre-instalados desnecessarios (Bing News/Weather, Solitaire, Teams, Skype, YourPhone, etc)?" `
        "Apps desnecessarios removidos" `
        {
            Registrar-Irreversivel "Apps pre-instalados removidos (Bing News/Weather, Solitaire, Teams, Skype, YourPhone, etc - reinstale pela Microsoft Store se precisar)"
            $removeApps = @(
                "*BingNews*", "*BingWeather*", "*GetHelp*", "*Getstarted*",
                "*MicrosoftOfficeHub*", "*MicrosoftSolitaireCollection*", "*People*",
                "*PowerAutomateDesktop*", "*Todos*", "*YourPhone*", "*MicrosoftTeams*",
                "*SkypeApp*", "*MixedReality*", "*WindowsMaps*"
            )
            foreach ($app in $removeApps) {
                Get-AppxPackage -AllUsers -Name $app | Remove-AppxPackage -AllUsers
                Get-AppxProvisionedPackage -Online | Where-Object { $_.DisplayName -like $app } | Remove-AppxProvisionedPackage -Online
            }
        } $modoRapido

    # 15. Efeitos visuais
    Executar-Se-Confirmado "Ajustar efeitos visuais para melhor desempenho?" `
        "Efeitos visuais ajustados" `
        {
            $visualPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\VisualEffects"
            New-Item -Path $visualPath -Force | Out-Null
            Capturar-Registro $visualPath "VisualFXSetting"
            Set-ItemProperty $visualPath "VisualFXSetting" -Type DWord -Value 2
        } $modoRapido

    # 16. Indexacao do Windows Search
    Executar-Se-Confirmado "Desativar a indexacao do Windows Search?" `
        "Indexacao do Windows Search desativada" `
        {
            Capturar-Servico "WSearch"
            Stop-Service "WSearch" -Force
            Set-Service "WSearch" -StartupType Disabled
        } $modoRapido

    # 17. Limpeza final de temporarios
    Executar-Se-Confirmado "Executar limpeza final de arquivos temporarios?" `
        "Arquivos temporarios removidos" `
        {
            Remove-Item "$env:TEMP\*" -Recurse -Force -ErrorAction SilentlyContinue
            Remove-Item "$env:SystemRoot\Temp\*" -Recurse -Force -ErrorAction SilentlyContinue
        } $modoRapido

    Salvar-Snapshot

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "DEBLOAT CONCLUIDO!") -ForegroundColor Green
    Write-Host (Centralizar "Reinicie o PC para aplicar todas as mudancas.") -ForegroundColor Gray
    Write-Host (Centralizar "Resumo: $script:ContAplicados aplicado(s), $script:ContPulados pulado(s), $script:ContFalhas falha(s)") -ForegroundColor Gray
    Write-Host (Centralizar "Nao gostou? Use a opcao [7] Reverter Ultima Otimizacao.") -ForegroundColor DarkGray
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

    if (-not (Confirmar "Deseja reverter essa otimizacao agora?")) {
        Write-Host ""
        Write-Host "  Nenhuma alteracao foi feita." -ForegroundColor Yellow
        Write-Host ""
        Linha "="
        return
    }

    Write-Host ""
    $revertidos = 0
    $falhas = 0
    $naoReversiveis = @()

    # Percorre de tras para frente (ordem inversa a de aplicacao)
    for ($i = $itensArray.Count - 1; $i -ge 0; $i--) {
        $item = $itensArray[$i]
        Try {
            switch ($item.Tipo) {
                "Registro" {
                    if ($item.Existia) {
                        Set-ItemProperty -Path $item.Caminho -Name $item.Nome -Value $item.ValorAnterior -ErrorAction Stop
                    } else {
                        Remove-ItemProperty -Path $item.Caminho -Name $item.Nome -ErrorAction SilentlyContinue
                    }
                    Write-Resultado $true "Registro restaurado: $($item.Nome)"
                    $revertidos++
                }
                "Servico" {
                    Set-Service -Name $item.Nome -StartupType $item.StartupTypeAnterior -ErrorAction Stop
                    if ($item.StatusAnterior -eq "Running") {
                        Start-Service -Name $item.Nome -ErrorAction SilentlyContinue
                    }
                    Write-Resultado $true "Servico restaurado: $($item.Nome)"
                    $revertidos++
                }
                "PlanoEnergia" {
                    powercfg /setactive $item.GuidAnterior
                    Write-Resultado $true "Plano de energia restaurado"
                    $revertidos++
                }
                "TarefaAgendada" {
                    schtasks.exe /Change /TN $item.Nome /Enable | Out-Null
                    Write-Resultado $true "Tarefa reativada: $($item.Nome)"
                    $revertidos++
                }
                "OneDrive" {
                    if (Test-Path $item.Caminho) {
                        Start-Process $item.Caminho -Wait
                        Write-Resultado $true "OneDrive: reinstalacao iniciada"
                        $revertidos++
                    } else {
                        Write-Resultado $false "OneDrive: instalador nao encontrado"
                        $falhas++
                    }
                }
                "Irreversivel" {
                    $naoReversiveis += $item.Descricao
                }
                default { }
            }
        } Catch {
            Write-Resultado $false "Falha ao reverter: $($item.Nome)"
            $falhas++
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
        Write-Host "  Para esses casos, use o Ponto de Restauracao do Windows criado" -ForegroundColor Gray
        Write-Host "  antes da otimizacao (Painel de Controle > Recuperacao > Restauracao" -ForegroundColor Gray
        Write-Host "  do Sistema)." -ForegroundColor Gray
    }

    # Arquiva o snapshot para nao tentar reverter a mesma acao duas vezes
    Try {
        $arquivoRevertido = Join-Path $script:SnapshotDir "revertido_$(Get-Date -Format 'yyyyMMdd_HHmmss').json"
        Move-Item -Path $script:SnapshotPath -Destination $arquivoRevertido -Force
    } Catch { }

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
function Obter-ItensInicializacao {
    $locais = @(
        @{ Caminho = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Usuario" },
        @{ Caminho = "HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Maquina" },
        @{ Caminho = "HKLM:\SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Run"; Escopo = "Maquina (32-bit)" }
    )
    $itens = @()
    foreach ($local in $locais) {
        if (Test-Path $local.Caminho) {
            $props = Get-ItemProperty -Path $local.Caminho
            $props.PSObject.Properties | Where-Object { $_.Name -notmatch '^PS' } | ForEach-Object {
                $itens += [PSCustomObject]@{
                    Nome    = $_.Name
                    Comando = "$($_.Value)"
                    Caminho = $local.Caminho
                    Escopo  = $local.Escopo
                }
            }
        }
    }
    return $itens
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

# Soma o tamanho de pastas de temporarios/cache conhecidas (para o relatorio de "ANALISAR PC")
function Obter-TamanhoTemporarios {
    $pastas = @("$env:TEMP", "$env:SystemRoot\Temp", "$env:SystemRoot\Prefetch")
    $totalBytes = 0
    foreach ($pasta in $pastas) {
        Try {
            $soma = (Get-ChildItem -Path $pasta -Recurse -Force -ErrorAction SilentlyContinue |
                     Measure-Object -Property Length -Sum).Sum
            if ($soma) { $totalBytes += $soma }
        } Catch { }
    }
    return $totalBytes
}

# Detecta se a unidade do sistema e um SSD (usado pela Otimizacao Inteligente)
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
        $qtdInicializacao = @(Obter-ItensInicializacao).Count
        $resultado.Inicializacao = [Math]::Max(10, 100 - ($qtdInicializacao * 8))
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
    $itensInicializacao = @(Obter-ItensInicializacao)
    $planoAtivo = (powercfg /getactivescheme) -join " "
    $nomePlano = if ($planoAtivo -match '\((.+)\)') { $matches[1] } else { "Desconhecido" }
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

    $score = Calcular-HealthScore
    $script:UltimoScore = $score
    Mostrar-HealthScore $score

    Write-Host ""
    Linha "="
    Write-Host (Centralizar "PROBLEMAS ENCONTRADOS") -ForegroundColor Yellow
    Linha "="

    $problemas = @()
    if ($tamanhoTemp -gt 500MB) { $problemas += "$(Format-Bytes $tamanhoTemp) de arquivos temporarios acumulados" }
    if ($itensInicializacao.Count -ge 5) { $problemas += "$($itensInicializacao.Count) programas iniciando junto com o Windows" }
    if ($searchAtivo) { $problemas += "Windows Search ativo (pode consumir recursos em HDs mais lentos)" }
    if ($nomePlano -notmatch "Alto Desempenho|Ultimate|Desempenho") { $problemas += "Plano de energia atual: $nomePlano" }
    if (-not $trimAtivo -and $ehSSD) { $problemas += "TRIM parece desativado no SSD" }
    if (Test-PendingReboot) { $problemas += "Ha uma reinicializacao pendente" }
    if ($score.Disco -lt 40) { $problemas += "Pouco espaco livre em disco" }

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
    Write-Host "  [1] Otimizacao Inteligente  - deixa o programa decidir o que aplicar" -ForegroundColor Cyan
    Write-Host "  [2] Versao Padrao           - limpeza e ajustes basicos" -ForegroundColor Cyan
    if ($itensInicializacao.Count -ge 5) {
        Write-Host "  [8] Gerenciar Inicializacao - reduzir programas no boot" -ForegroundColor Cyan
    }
    Write-Host ""

    if (Confirmar "Deseja rodar a Otimizacao Inteligente agora com base nesse diagnostico?") {
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
        Write-Status "ok" "Plano de energia de Alto Desempenho + Modo de Jogo (perfil com jogos instalados)"
    } else {
        Write-Status "ok" "Plano de energia Equilibrado/Alto Desempenho (sem tweaks agressivos de jogo)"
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
        @{ Nome = "Ajustando plano de energia"; Acao = {
                Capturar-PlanoEnergia
                if ($perfil -eq "Jogos" -or $perfil -eq "Misto") {
                    $ultimatePlan = powercfg /duplicatescheme e9a42b02-d5df-448d-aa00-03f14749eb61
                    if ($ultimatePlan -match "([0-9a-fA-F-]{36})") { powercfg /setactive $matches[1] }
                    else { powercfg /setactive SCHEME_MIN }
                } else {
                    powercfg /setactive SCHEME_MIN
                }
            } }
        @{ Nome = "Limpando cache DNS"; Acao = { ipconfig /flushdns } }
        @{ Nome = "Otimizando/TRIM das unidades de disco"; Acao = {
                Get-Volume | Where-Object { $_.DriveLetter } | ForEach-Object {
                    Optimize-Volume -DriveLetter $_.DriveLetter -ReTrim
                }
            } }
        @{ Nome = "Limpeza de disco (cleanmgr)"; Acao = { Start-Process cleanmgr.exe -ArgumentList "/sagerun:1" -WindowStyle Hidden } }
    )
    if ($perfil -eq "Jogos" -or $perfil -eq "Misto") {
        $etapas += @{ Nome = "Ativando Modo de Jogo do Windows"; Acao = {
                New-Item -Path "HKCU:\Software\Microsoft\GameBar" -Force | Out-Null
                Capturar-Registro "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled"
                Set-ItemProperty "HKCU:\Software\Microsoft\GameBar" "AutoGameModeEnabled" 1 -Type DWord
            } }
    }
    $etapas += @{ Nome = "Verificando arquivos do sistema (sfc /verifyonly)"; Acao = { sfc /verifyonly } }

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
function Executar-VerificarSFC {
    Write-Secao "Verificando arquivos do sistema (SFC)"
    Write-Host "  Isso pode demorar alguns minutos..." -ForegroundColor Gray
    Write-Host ""
    sfc /scannow
    Write-Host ""
    Linha "="
}

function Executar-RepararDISM {
    Write-Secao "Reparando imagem do Windows (DISM)"
    Write-Host "  Isso pode demorar varios minutos e precisa de internet..." -ForegroundColor Gray
    Write-Host ""
    DISM /Online /Cleanup-Image /RestoreHealth
    Write-Host ""
    Linha "="
}

function Executar-VerificarDisco {
    Write-Secao "Verificando o disco (CHKDSK)"
    Write-Host "  Executando uma verificacao online (sem precisar reiniciar)..." -ForegroundColor Gray
    Write-Host ""
    chkdsk $env:SystemDrive /scan
    Write-Host ""
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

function Medir-VelocidadeDisco {
    $pasta = "$env:TEMP\otimizador_bench"
    New-Item -Path $pasta -ItemType Directory -Force | Out-Null
    $arquivo = Join-Path $pasta "teste.tmp"
    $tamanhoMB = 100
    $bloco = New-Object byte[] (1MB)
    (New-Object Random).NextBytes($bloco)

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $fs = [System.IO.File]::Open($arquivo, [System.IO.FileMode]::Create)
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
    Write-Host "  Medindo CPU, RAM, disco e tempo de boot..." -ForegroundColor Gray
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
    $discoLivrePercent = $null
    Try {
        $letra = $env:SystemDrive.TrimEnd(":")
        $vol = Get-Volume -DriveLetter $letra -ErrorAction Stop
        $discoLivrePercent = [Math]::Round(($vol.SizeRemaining / $vol.Size) * 100)
    } Catch { }

    Write-Host "  CPU" -ForegroundColor Cyan
    Barra-Score $cpuPercent
    Write-Host "  RAM" -ForegroundColor Cyan
    Barra-Score $ramPercent
    Write-Host ""
    Write-Host "  DISCO" -ForegroundColor Cyan
    Write-Status "info" "Leitura:  $($disco.Leitura) MB/s"
    Write-Status "info" "Escrita:  $($disco.Escrita) MB/s"
    if ($null -ne $discoLivrePercent) { Write-Status "info" "Espaco usado: $(100 - $discoLivrePercent)%" }
    Write-Host ""
    if ($bootSeg) {
        Write-Host "  BOOT" -ForegroundColor Cyan
        Write-Status "info" "Ultima inicializacao: $bootSeg s"
    }

    $resultadoAtual = [PSCustomObject]@{
        Data          = (Get-Date).ToString("dd/MM/yyyy HH:mm:ss")
        CPU           = $cpuPercent
        RAM           = $ramPercent
        DiscoLeitura  = $disco.Leitura
        DiscoEscrita  = $disco.Escrita
        DiscoUsado    = if ($null -ne $discoLivrePercent) { 100 - $discoLivrePercent } else { $null }
        BootSegundos  = $bootSeg
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
        } Catch { }
    } else {
        Write-Host ""
        Write-Host "  Este e o primeiro benchmark salvo. Rode novamente depois de" -ForegroundColor DarkGray
        Write-Host "  otimizar o PC para ver a comparacao antes -> depois." -ForegroundColor DarkGray
    }

    New-Item -Path (Split-Path $script:BenchmarkPath) -ItemType Directory -Force | Out-Null
    $resultadoAtual | ConvertTo-Json | Out-File -FilePath $script:BenchmarkPath -Encoding UTF8 -Force

    Write-Host ""
    Write-Host "  Obs: isso NAO garante ganho de FPS - mede uso de CPU/RAM/disco e o boot." -ForegroundColor DarkGray
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
        @{ T = " [3] VERSAO GAMER";                                     C = "Magenta" }
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
Set-Aparencia

try {
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
}
finally {
    Write-Host ""
    Read-Host "  Pressione ENTER para sair"
}
