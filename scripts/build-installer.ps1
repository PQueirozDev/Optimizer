param(
    [string]$Version = "1.0.0",
    [string]$Configuration = "Release",
    [string]$Runtime = "win-x64",
    [switch]$SkipInstaller
)

$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$projectPath = Join-Path $repoRoot "PQueirozOptimizer\PQueirozOptimizer.csproj"
$publishDir = Join-Path $repoRoot "artifacts\publish\$Runtime"
$installerDir = Join-Path $repoRoot "artifacts\installer"
$issPath = Join-Path $repoRoot "installer\PQueirozOptimizer.iss"

function Assert-InRepo([string]$PathToCheck) {
    $fullPath = [System.IO.Path]::GetFullPath($PathToCheck)
    $rootPath = [System.IO.Path]::GetFullPath($repoRoot)
    if (-not $fullPath.StartsWith($rootPath, [System.StringComparison]::OrdinalIgnoreCase)) {
        throw "Caminho fora do repositorio: $fullPath"
    }
}

function Clear-Directory([string]$PathToClear) {
    Assert-InRepo $PathToClear
    if (Test-Path -LiteralPath $PathToClear) {
        Remove-Item -LiteralPath $PathToClear -Recurse -Force
    }
    New-Item -ItemType Directory -Path $PathToClear -Force | Out-Null
}

function Find-InnoCompiler {
    $command = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($command) {
        return $command.Source
    }

    $knownPaths = @(
        "${env:ProgramFiles(x86)}\Inno Setup 6\ISCC.exe",
        "$env:ProgramFiles\Inno Setup 6\ISCC.exe",
        "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe"
    )

    foreach ($path in $knownPaths) {
        if ($path -and (Test-Path -LiteralPath $path)) {
            return $path
        }
    }

    return $null
}

Write-Host "==> Publicando Qrztweaks $Version ($Runtime)"
Clear-Directory $publishDir
Clear-Directory $installerDir

dotnet restore $projectPath
# Sem compactar o executável: descompactar o runtime custava ~0,4 s em toda abertura (o instalador já é comprimido).
# (ReadyToRun foi medido: ganha ~90 ms por abertura mas deixa a 1ª abertura após instalar ~1 s mais lenta.)
dotnet publish $projectPath `
    --configuration $Configuration `
    --runtime $Runtime `
    --self-contained true `
    --output $publishDir `
    /p:PublishSingleFile=true `
    /p:IncludeNativeLibrariesForSelfExtract=true `
    /p:DebugType=None `
    /p:DebugSymbols=false `
    /p:Version=$Version `
    /p:AssemblyVersion=$Version.0 `
    /p:FileVersion=$Version.0 `
    /p:InformationalVersion=$Version

$exePath = Join-Path $publishDir "PQueirozOptimizer.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Executavel publicado nao encontrado: $exePath"
}

if ($SkipInstaller) {
    Write-Host "==> Instalador ignorado por -SkipInstaller"
    Write-Host "Publicacao: $publishDir"
    exit 0
}

$iscc = Find-InnoCompiler
if (-not $iscc) {
    throw "Inno Setup Compiler (ISCC.exe) nao encontrado. Instale o Inno Setup 6: winget install JRSoftware.InnoSetup"
}

Write-Host "==> Gerando instalador com Inno Setup"
& $iscc "/DMyAppVersion=$Version" "/DMyAppPublisher=Pedro Queiroz" $issPath

$setupPath = Join-Path $installerDir "Qrztweaks-Setup-v$Version.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Instalador nao encontrado: $setupPath"
}

Write-Host "==> Build concluido"
Write-Host "Publicacao: $publishDir"
Write-Host "Instalador: $setupPath"
