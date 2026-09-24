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
$allowlists = 'TarefasMMCSS|RegistroPermitido|RegistroPermitidoPadroes|ServicosPermitidos|TiposRegistroPermitidos|StartupTypesPermitidos|InstaladoresOneDrive|RunKeysPermitidas|LimiteHistorico'
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
