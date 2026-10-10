# Executa os testes antes de criar o commit e a tag que publica o instalador.
$ErrorActionPreference = 'Stop'
Set-Location (Split-Path -Parent $PSScriptRoot)

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$Command falhou (codigo $LASTEXITCODE). Publicacao interrompida." }
}

$branch = git branch --show-current
if ($LASTEXITCODE -ne 0 -or $branch -ne 'main') { throw 'Execute na branch main.' }
[xml]$props = Get-Content Directory.Build.props -Raw
$version = [string]$props.Project.PropertyGroup.Version
if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'Versao invalida em Directory.Build.props.' }
$tag = "v$version"

Invoke-Checked git @('fetch', 'origin', '--tags')
$existing = git tag --list $tag
if ($LASTEXITCODE -ne 0) { throw 'Falha ao consultar tags.' }
if ($existing) { throw "A tag $tag ja existe. Nenhuma tag sera sobrescrita." }
Invoke-Checked git @('merge-base', '--is-ancestor', 'origin/main', 'HEAD')

Invoke-Checked powershell @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'tests/Verify-PowerShell.ps1')
Invoke-Checked dotnet @('run', '--project', 'tests/Optimizer.Verification', '--', 'artifacts/verification')
Invoke-Checked dotnet @('run', '--project', 'tests/Optimizer.Verification', '--no-build', '--', 'artifacts/verification', '--branding')
Invoke-Checked git @('diff', '--check')
Invoke-Checked powershell @('-NoProfile', '-ExecutionPolicy', 'Bypass', '-File', 'scripts/release-notes.ps1', '-Version', $version, '-Output', 'artifacts/verification/release-notes.md')

Invoke-Checked git @('add', '.github/workflows/release.yml', 'Directory.Build.props', 'PQueirozOptimizer', 'README.md', 'CHANGELOG.md', 'docs', 'installer', 'scripts', 'site', 'tests/Optimizer.Verification', 'tools')
# Só cria o commit de release se houver algo novo (a versão pode já ter sido commitada antes)
git diff --cached --quiet
if ($LASTEXITCODE -ne 0) { Invoke-Checked git @('commit', '-m', "Release ${tag}") }
Invoke-Checked git @('tag', '-a', $tag, '-m', "Qrztweaks $tag")
Invoke-Checked git @('push', '--atomic', 'origin', 'HEAD:main', "refs/tags/$tag")

Write-Host "Enviado: $tag. A publicacao do instalador depende do sucesso do workflow."
Write-Host 'Acompanhe: https://github.com/PQueirozDev/Optimizer/actions'
