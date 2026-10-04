param([string]$OutputDirectory = "$PSScriptRoot\..\artifacts\powershell-verification")
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path $PSScriptRoot -Parent
$tokens = $null; $errors = $null
$ast = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'Otimizador_de_PC.ps1'), [ref]$tokens, [ref]$errors)
if ($errors.Count) { throw ($errors | Out-String) }
# Load function definitions only. Never execute elevation, menus or an optimization.
foreach ($definition in $ast.FindAll({ param($node) $node -is [System.Management.Automation.Language.FunctionDefinitionAst] }, $false)) {
    . ([scriptblock]::Create($definition.Extent.Text))
}
function Assert($condition, $name) { if (-not $condition) { throw $name }; Write-Host "PASS $name" }
function Write-Secao($text) { }
function Linha($text) { }
function Centralizar($text) { return $text }
function Write-Resultado($ok, $text) { }
function Write-Pulado($text) { }
# Load the real allowlists so the tests exercise the same rules the script uses.
$allowlists = 'TarefasMMCSS|RegistroPermitido|RegistroPermitidoPadroes|ServicosPermitidos|TiposRegistroPermitidos|StartupTypesPermitidos|InstaladoresOneDrive|RunKeysPermitidas|LimiteHistorico|Plano\w+|InicializacaoAprovada|PerfilCleanmgr|CategoriasCleanmgr'
foreach ($assignment in $ast.EndBlock.Statements | Where-Object { $_ -is [System.Management.Automation.Language.AssignmentStatementAst] -and $_.Left.Extent.Text -match "^\`$script:($allowlists)$" }) {
    . ([scriptblock]::Create($assignment.Extent.Text))
}
if (-not $script:ServicosPermitidos) { throw 'Allowlists were not loaded' }
$UiMode = $true
$script:SnapshotDir = [IO.Path]::GetFullPath($OutputDirectory)
if (Test-Path -LiteralPath $script:SnapshotDir) { Get-ChildItem -LiteralPath $script:SnapshotDir -Filter '*.json' | Remove-Item -Force }
New-Item -ItemType Directory -Force -Path $script:SnapshotDir | Out-Null
$script:SnapshotPath = Join-Path $script:SnapshotDir 'ultima_otimizacao.json'
$script:SnapshotAtual = @()
$script:SnapshotNome = 'Test fixture'
$script:ContAplicados = 0; $script:ContPulados = 0; $script:ContFalhas = 0
$script:SelectedSteps = @('good', 'bad', 'native')
$script:ran = @()
Executar-Etapas 'Test' @(
    @{ Nome='good'; Acao={ $script:ran += 'good' } },
    @{ Nome='skip'; Acao={ throw 'Must not execute' } },
    @{ Nome='bad'; Acao={ Write-Error 'Expected failure' } },
    @{ Nome='native'; Acao={ $global:LASTEXITCODE=7 } }
)
Assert ($script:ContAplicados -eq 1 -and $script:ContPulados -eq 1 -and $script:ContFalhas -eq 2) 'Partial failure and skipped steps reported accurately'
$blocked = $false
try { Executar-Etapas 'Ponto de restauracao' @(@{ Nome='restore'; Acao={ throw 'Expected restore failure' } }) } catch { $blocked = $true }
Assert $blocked 'Restore point failure stops execution'

function Get-Service($Name) { [pscustomobject]@{ StartType='Manual'; Status='Stopped' } }
Iniciar-Snapshot 'Fixture'
Capturar-Servico 'Fax'
$saved = Get-Content -Raw -LiteralPath $script:SnapshotPath | ConvertFrom-Json
Assert ($saved.Itens[0].Nome -eq 'Fax') 'Service backup saved before mutation'
$rejected = $false
try { Capturar-Servico 'servico-desconhecido' } catch { $rejected = $true }
Assert $rejected 'Capture refuses services outside the allowlist'
Iniciar-Snapshot 'Next fixture'
Assert (@(Get-ChildItem -LiteralPath $script:SnapshotDir -Filter 'historico_*.json').Count -gt 0) 'Previous backup retained in history'
Assert (@($script:SnapshotAtual | Where-Object { $_.Nome -eq 'Fax' }).Count -eq 1) 'Unreverted backup is carried into the next run'
Capturar-Servico 'Fax'
Assert (@($script:SnapshotAtual | Where-Object { $_.Nome -eq 'Fax' }).Count -eq 1) 'First capture wins so revert restores the original state'
Assert ($script:SnapshotNome -eq 'Fixture + Next fixture') 'Merged backup keeps both run names'

# Reversão completa: sem ajustes escolhidos (com eles, só os itens desses ajustes voltam)
$script:SelectedSteps = $null
function Set-Service($Name, $StartupType) { if ($Name -eq 'Fax') { throw 'Expected service restore failure' } }
function Stop-Service($Name, [switch]$Force) { }
$script:startedProcesses = @()
function Start-Process { $script:startedProcesses += "$args"; [pscustomobject]@{ ExitCode = 0 } }
$script:SnapshotNome = 'Retry fixture'
$script:SnapshotAtual = @(
    [pscustomobject]@{ Tipo='Servico'; Nome='Fax'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' },
    [pscustomobject]@{ Tipo='Servico'; Nome='Spooler'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' },
    [pscustomobject]@{ Tipo='OneDrive'; Caminho='C:\Users\Public\evil.exe' },
    [pscustomobject]@{ Tipo='Registro'; Caminho='HKLM:\SOFTWARE\Microsoft\Windows\CurrentVersion\Run'; Nome='evil'; Existia=$true; ValorAnterior='C:\evil.exe'; TipoAnterior='String' }
)
Salvar-Snapshot
$script:ContFalhas = 0
Reverter-UltimaOtimizacao
$retry = Get-Content -Raw -LiteralPath $script:SnapshotPath | ConvertFrom-Json
Assert ($script:startedProcesses.Count -eq 0) 'Revert never runs executables outside the allowlist'
Assert ($script:ContFalhas -eq 1) 'Revert failure propagates to operation result'
Assert (@($retry.Itens).Count -eq 1 -and $retry.Itens[0].Nome -eq 'Fax') 'Only failed restoration remains pending'
function Set-Service($Name, $StartupType) { }
$script:ContFalhas = 0
Reverter-UltimaOtimizacao
Assert (-not (Test-Path -LiteralPath $script:SnapshotPath)) 'Successful retry archives pending backup'

# Desfazer um ajuste só: os itens dos outros ajustes continuam no backup
$script:SnapshotNome = 'Per-step fixture'
$script:SnapshotAtual = @(
    [pscustomobject]@{ Tipo='Servico'; Etapa='Ajuste A'; Nome='Fax'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' },
    [pscustomobject]@{ Tipo='Servico'; Etapa='Ajuste B'; Nome='Spooler'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' }
)
Salvar-Snapshot
$script:SelectedSteps = @('Ajuste A')
$script:ContFalhas = 0
Reverter-UltimaOtimizacao
$restante = Get-Content -Raw -LiteralPath $script:SnapshotPath | ConvertFrom-Json
Assert ($script:ContFalhas -eq 0 -and @($restante.Itens).Count -eq 1 -and $restante.Itens[0].Etapa -eq 'Ajuste B') 'Per-step revert keeps the other steps in the backup'
$script:SelectedSteps = @('Ajuste B')
Reverter-UltimaOtimizacao
Assert (-not (Test-Path -LiteralPath $script:SnapshotPath)) 'Reverting the last step archives the backup'
$script:SelectedSteps = $null

# Itens novos guardam de qual ajuste vieram
$script:SnapshotAtual = @()
$script:EtapaAtual = 'Ajuste C'
Capturar-Hibernacao
Assert ($script:SnapshotAtual[0].Tipo -eq 'Hibernacao' -and $script:SnapshotAtual[0].Etapa -eq 'Ajuste C') 'Captured items record their step'
$script:SnapshotAtual = @(); $script:EtapaAtual = $null
Remove-Item -LiteralPath $script:SnapshotPath -Force -ErrorAction SilentlyContinue

1..($script:LimiteHistorico + 5) | ForEach-Object { '{}' | Out-File -LiteralPath (Join-Path $script:SnapshotDir "historico_extra$_.json") }
Limpar-HistoricoAntigo
Assert (@(Get-ChildItem -LiteralPath $script:SnapshotDir -Filter '*.json' | Where-Object { $_.Name -like 'historico_*' -or $_.Name -like 'revertido_*' }).Count -eq $script:LimiteHistorico) 'History is capped'
Write-Host 'All tests used isolated files and mocked system mutations.'

# New-Item -Force apaga chaves existentes; Garantir-Chave nunca pode fazer isso
$chaveTeste = 'HKCU:\Software\PQO-Verificacao'
Remove-Item $chaveTeste -Recurse -Force -ErrorAction SilentlyContinue
New-Item -Path "$chaveTeste\Sub" -Force | Out-Null
Set-ItemProperty $chaveTeste -Name 'Existente' -Value 1 -Type DWord
Garantir-Chave $chaveTeste
Assert (((Get-ItemProperty $chaveTeste).Existente -eq 1) -and (Test-Path "$chaveTeste\Sub")) 'Garantir-Chave preserves existing values and subkeys'
Remove-Item $chaveTeste -Recurse -Force
$ast2 = [System.Management.Automation.Language.Parser]::ParseFile((Join-Path $projectRoot 'Otimizador_de_PC.ps1'), [ref]$null, [ref]$null)
function Test-ProtegidoPorTestPath($node) {
    for ($p = $node.Parent; $p; $p = $p.Parent) {
        if ($p -is [System.Management.Automation.Language.IfStatementAst] -and ($p.Clauses | Where-Object { $_.Item1.Extent.Text -match 'Test-Path' })) { return $true }
        if ($p -is [System.Management.Automation.Language.ScriptBlockAst]) { return $false }
    }
    return $false
}
$perigosos = $ast2.FindAll({ param($n) $n -is [System.Management.Automation.Language.CommandAst] -and $n.GetCommandName() -eq 'New-Item' -and $n.Extent.Text -match '-Force' -and $n.Extent.Text -notmatch 'Directory' }, $true) | Where-Object { -not (Test-ProtegidoPorTestPath $_) }
Assert (@($perigosos).Count -eq 0) 'No unguarded New-Item -Force on registry keys'
Assert ($script:TarefasMMCSS.Count -eq 8 -and $script:TarefasMMCSS['Pro Audio'].Priority -eq 1) 'MMCSS defaults cover all Windows tasks'
$msi = 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2484&SUBSYS_146B10DE&REV_A1\4&1a2b3c4d&0&0019\Device Parameters\Interrupt Management\MessageSignaledInterruptProperties'
Assert (Test-RegistroPermitido $msi 'MSISupported') 'MSI mode key of a PCI device is allowed'
Assert (-not (Test-RegistroPermitido $msi 'MessageNumberLimit')) 'Only MSISupported is allowed under the MSI key'
Assert (-not (Test-RegistroPermitido 'HKLM:\SYSTEM\CurrentControlSet\Enum\PCI\VEN_10DE&DEV_2484\x\Device Parameters' 'MSISupported')) 'Other device keys stay blocked'
Assert (-not (Test-ItemPermitido ([pscustomobject]@{ Tipo='Registro'; Caminho='HKLM:\SYSTEM\CurrentControlSet\Services\evil'; Nome='MSISupported'; Existia=$false }))) 'Revert refuses MSI value outside a PCI device key'
Assert ((Obter-VelocidadeNominalRAM 'CMK16GX4M2B3200C16') -eq 3200 -and (Obter-VelocidadeNominalRAM 'KF436C16BB/8') -eq 3600 -and (Obter-VelocidadeNominalRAM 'F5-6000J3038F16G') -eq 6000) 'RAM rated speed parsed from part number'
Assert ($null -eq (Obter-VelocidadeNominalRAM 'M378A1K43CB2-CTD')) 'Unknown part number yields no rated speed'
function Get-Service($Name) { $null }
$script:SnapshotAtual = @()
Desativar-Servico 'Fax'
Assert (@($script:SnapshotAtual).Count -eq 0) 'Missing service is skipped without capture or failure'
Assert ($script:RegistroPermitido -contains 'HKCU:\Control Panel\Desktop' -and $script:RegistroPermitido -contains 'HKCU:\Control Panel\Desktop\WindowMetrics') 'Visual effects keys can be restored'

# Tarefas agendadas removidas do Windows 11 recente nao podem interromper o Debloat
function Get-ScheduledTask($TaskPath, $TaskName) { if ($TaskName -eq 'Existe') { [pscustomobject]@{ State = 'Ready' } } }
$script:SnapshotAtual = @()
Assert (-not (Capturar-TarefaAgendada '\Microsoft\Windows\Teste\Removida')) 'Missing scheduled task is reported as absent instead of failing'
Assert ((Capturar-TarefaAgendada '\Microsoft\Windows\Teste\Existe') -and $script:SnapshotAtual[-1].HabilitadaAnterior -eq $true) 'Existing scheduled task state is captured'

# Servicos com inicio atrasado (Windows Search) voltam como estavam
& {
    function Get-Service($Name) { [pscustomobject]@{ StartType = 'Automatic'; Status = 'Running' } }
    function Get-ItemProperty { [pscustomobject]@{ DelayedAutostart = 1 } }
    $script:SnapshotAtual = @()
    Capturar-Servico 'WSearch'
    Assert ($script:SnapshotAtual[0].StartupTypeAnterior -eq 'AutomaticDelayedStart') 'Delayed start is captured before the service changes'
}
& {
    $script:tiposAplicados = @(); $script:scArgs = $null
    function Set-Service($Name, $StartupType) { $script:tiposAplicados += "$Name=$StartupType" }
    function Start-Service($Name) { }
    function sc.exe { $script:scArgs = "$args"; $global:LASTEXITCODE = 0 }
    $script:SnapshotNome = 'Delayed fixture'
    $script:SnapshotAtual = @([pscustomobject]@{ Tipo = 'Servico'; Nome = 'WSearch'; StartupTypeAnterior = 'AutomaticDelayedStart'; StatusAnterior = 'Running' })
    Salvar-Snapshot
    $script:ContFalhas = 0
    Reverter-UltimaOtimizacao
    Assert ($script:tiposAplicados -contains 'WSearch=Automatic' -and $script:scArgs -eq 'config WSearch start= delayed-auto' -and $script:ContFalhas -eq 0) 'Delayed-start service is restored as delayed start'
}

# OneDrive: no Windows 11 o desinstalador so e achado pelo registro; nunca executa algo sem assinatura da Microsoft
& {
    function Get-ItemProperty { if ("$args" -like '*HKCU:*') { [pscustomobject]@{ UninstallString = '"C:\Users\x\AppData\Local\Microsoft\OneDrive\25.1\OneDriveSetup.exe"  /uninstall ' } } }
    function Test-Path { "$args" -like '*C:\Users\x\*' }
    function Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Microsoft Corporation, O=Microsoft Corporation, L=Redmond' } } }
    $desinstalador = Obter-DesinstaladorOneDrive
    Assert ($desinstalador.Exe -eq 'C:\Users\x\AppData\Local\Microsoft\OneDrive\25.1\OneDriveSetup.exe' -and $desinstalador.Argumentos -eq '/uninstall') 'Per-user OneDrive uninstaller found in the registry (Windows 11)'
    function Get-AuthenticodeSignature { [pscustomobject]@{ Status = 'Valid'; SignerCertificate = [pscustomobject]@{ Subject = 'CN=Outro, O=Outra Empresa' } } }
    $recusado = $false
    try { Obter-DesinstaladorOneDrive | Out-Null } catch { $recusado = $true }
    Assert $recusado 'OneDrive uninstaller without a Microsoft signature is never run'
    function Get-ItemProperty { }
    function Test-Path { $false }
    Assert ($null -eq (Obter-DesinstaladorOneDrive)) 'OneDrive not installed is not treated as a failure'
}

# Plano Alto Desempenho apagado por outro otimizador e recriado; sem ele (Modern Standby) a etapa nao falha
& {
    $script:chamadas = @()
    function powercfg { $script:chamadas += "$args"; $global:LASTEXITCODE = 0; switch ($args[0]) { '/list' { 'Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *' } '/duplicatescheme' { 'Power Scheme GUID: 11111111-2222-3333-4444-555555555555  (High performance)' } } }
    Assert ((Ativar-PlanoAltoDesempenho) -and $script:chamadas -contains '/setactive 11111111-2222-3333-4444-555555555555') 'Deleted High Performance plan is recreated and activated'
    function powercfg { $global:LASTEXITCODE = 0; switch ($args[0]) { '/list' { 'Power Scheme GUID: 381b4222-f694-41f0-9685-ff5bb260df2e  (Balanced) *' } '/duplicatescheme' { 'Unable to perform operation.'; $global:LASTEXITCODE = 1 } } }
    Assert (-not (Ativar-PlanoAltoDesempenho) -and $LASTEXITCODE -eq 0) 'Balanced-only laptop keeps its plan without failing the step'
}

# cleanmgr /sagerun sem categorias marcadas nao apaga nada; o perfil proprio evita as categorias perigosas
Assert (-not ($script:CategoriasCleanmgr | Where-Object { $_ -in 'Recycle Bin', 'DownloadsFolder', 'Previous Installations', 'Windows ESD installation files', 'D3D Shader Cache', 'User file versions', 'Update Cleanup' })) 'Disk cleanup never touches Recycle Bin, Downloads, Windows.old, reset files or shader cache'
& {
    $script:marcadas = @(); $script:argumentosCleanmgr = $null
    function Test-Path { $true }
    function Set-ItemProperty($LiteralPath, $Name, $Value) { $script:marcadas += "$LiteralPath|$Name|$Value" }
    function Start-Process($FilePath, $ArgumentList) { $script:argumentosCleanmgr = $ArgumentList; [pscustomobject]@{ Id = 0 } | Add-Member -MemberType ScriptMethod -Name WaitForExit -Value { param($ms) $true } -PassThru }
    Executar-Cleanmgr
    Assert ($script:argumentosCleanmgr -eq '/sagerun:4242' -and @($script:marcadas | Where-Object { $_ -like '*\Temporary Files|StateFlags4242|2' }).Count -eq 1) 'Disk cleanup marks its own profile before running'
}

# Itens desativados pelo Gerenciador de Tarefas nao contam como "iniciando com o Windows"
$chaveAprovados = 'HKCU:\Software\PQO-Verificacao-Aprovados'
New-Item -Path $chaveAprovados -Force | Out-Null
New-ItemProperty -Path $chaveAprovados -Name 'Desligado' -Value ([byte[]](3,0,0,0,0,0,0,0,0,0,0,0)) -PropertyType Binary | Out-Null
New-ItemProperty -Path $chaveAprovados -Name 'Ligado' -Value ([byte[]](2,0,0,0,0,0,0,0,0,0,0,0)) -PropertyType Binary | Out-Null
$aprovados = Get-Item -LiteralPath $chaveAprovados
Assert (-not (Test-InicializacaoAtiva $aprovados 'Desligado') -and (Test-InicializacaoAtiva $aprovados 'Ligado') -and (Test-InicializacaoAtiva $aprovados 'SemMarca') -and (Test-InicializacaoAtiva $null 'x')) 'Startup items disabled in Task Manager are not counted'
Remove-Item $chaveAprovados -Recurse -Force

# Linhas de diagnostico saem marcadas para o app colori-las
Assert ("$(Write-Status 'warn' 'Pouco espaco' 6>&1)" -eq '[AVISO] Pouco espaco' -and "$(Write-Status 'ok' 'TRIM: Ativo' 6>&1)" -eq '[OK] TRIM: Ativo') 'Diagnostic lines are tagged for the app'

# Ryzen X3D com dois CCDs mantem Game Bar e plano Equilibrado; os de um CCD seguem a otimizacao normal
& {
    function Get-ItemProperty { [pscustomobject]@{ ProcessorNameString = $script:cpuTeste } }
    $script:cpuTeste = 'AMD Ryzen 9 7950X3D 16-Core Processor'
    $duplo = Test-X3dDuploCcd
    $script:cpuTeste = 'AMD Ryzen 7 9800X3D 8-Core Processor'
    $simples = Test-X3dDuploCcd
    $script:cpuTeste = 'Intel(R) Core(TM) i7-10700F CPU @ 2.90GHz'
    Assert ($duplo -and -not $simples -and -not (Test-X3dDuploCcd)) 'Dual-CCD X3D detected; single-CCD X3D and other CPUs are not'
}
