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
$UiMode = $true
$script:SnapshotDir = [IO.Path]::GetFullPath($OutputDirectory)
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
Capturar-Servico 'fixture-service'
$saved = Get-Content -Raw -LiteralPath $script:SnapshotPath | ConvertFrom-Json
Assert ($saved.Itens[0].Nome -eq 'fixture-service') 'Service backup saved before mutation'
Iniciar-Snapshot 'Next fixture'
Assert (@(Get-ChildItem -LiteralPath $script:SnapshotDir -Filter 'historico_*.json').Count -gt 0) 'Previous backup retained in history'

function Set-Service($Name, $StartupType) { if ($Name -eq 'bad') { throw 'Expected service restore failure' } }
function Stop-Service($Name, [switch]$Force) { }
$script:SnapshotNome = 'Retry fixture'
$script:SnapshotAtual = @(
    [pscustomobject]@{ Tipo='Servico'; Nome='bad'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' },
    [pscustomobject]@{ Tipo='Servico'; Nome='good'; StartupTypeAnterior='Manual'; StatusAnterior='Stopped' }
)
Salvar-Snapshot
$script:ContFalhas = 0
Reverter-UltimaOtimizacao
$retry = Get-Content -Raw -LiteralPath $script:SnapshotPath | ConvertFrom-Json
Assert ($script:ContFalhas -eq 1) 'Revert failure propagates to operation result'
Assert (@($retry.Itens).Count -eq 1 -and $retry.Itens[0].Nome -eq 'bad') 'Only failed restoration remains pending'
function Set-Service($Name, $StartupType) { }
$script:ContFalhas = 0
Reverter-UltimaOtimizacao
Assert (-not (Test-Path -LiteralPath $script:SnapshotPath)) 'Successful retry archives pending backup'
Write-Host 'All tests used isolated files and mocked system mutations.'

