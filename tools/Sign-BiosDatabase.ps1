<#
.SYNOPSIS
  Assina o banco do BIOS Advisor (bios-db/bios-db.json) para o app aceitar a versão publicada no GitHub.

.DESCRIPTION
  O app baixa bios-db.json + bios-db.json.sig e só usa o banco se a assinatura RSA-SHA256 conferir com a chave pública
  embutida (BiosDatabase.PublicKeyBlob) e a "version" for maior que a atual. A assinatura é feita sobre o texto
  normalizado (sem BOM, quebras de linha LF), então vale tanto no Windows (CRLF) quanto no GitHub (LF).

  Fluxo para publicar uma placa nova:
    1. Edite bios-db/bios-db.json (caminhos SÓ com fonte oficial) e aumente "version".
    2. .\tools\Sign-BiosDatabase.ps1
    3. Faça commit de bios-db.json e bios-db.json.sig e dê push no main. Os apps recebem em até 6 h, sem release.

  -NewKey cria o par de chaves (uma vez só). A chave privada fica em private\ (fora do git): guarde uma cópia segura.
#>
param(
    [switch]$NewKey,
    [switch]$Verify
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
$privateKeyPath = Join-Path $root 'private\bios-db-rsa-private.blob'
$publicKeyPath = Join-Path $root 'private\bios-db-rsa-public.txt'
$dbPath = Join-Path $root 'bios-db\bios-db.json'
$sigPath = "$dbPath.sig"

function Get-CanonicalBytes([string]$path) {
    $text = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8).TrimStart([char]0xFEFF).Replace("`r`n", "`n")
    return [System.Text.Encoding]::UTF8.GetBytes($text)
}

if ($NewKey) {
    if (Test-Path -LiteralPath $privateKeyPath) { throw "Já existe uma chave em $privateKeyPath. Apague-a manualmente se quiser mesmo trocar (os apps publicados deixam de aceitar bancos novos)." }
    New-Item -ItemType Directory -Force (Split-Path $privateKeyPath) | Out-Null
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(2048)
    try {
        [System.IO.File]::WriteAllBytes($privateKeyPath, $rsa.ExportCspBlob($true))
        $public = [Convert]::ToBase64String($rsa.ExportCspBlob($false))
        Set-Content -LiteralPath $publicKeyPath -Value $public -NoNewline
        Write-Host "Chave criada. Cole a chave pública em BiosDatabase.PublicKeyBlob:"
        Write-Host $public
    } finally { $rsa.Dispose() }
    return
}

# Confere o JSON antes de assinar (versão e formato)
$json = Get-Content -LiteralPath $dbPath -Raw -Encoding UTF8 | ConvertFrom-Json
if ($json.format -ne 1) { throw 'bios-db.json: "format" precisa ser 1.' }
if (-not ($json.version -is [int] -or $json.version -is [long]) -or $json.version -lt 1) { throw 'bios-db.json: "version" precisa ser um inteiro >= 1.' }

$bytes = Get-CanonicalBytes $dbPath
$sha = [System.Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')

if ($Verify) {
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    try {
        $source = [System.IO.File]::ReadAllText((Join-Path $root 'PQueirozOptimizer\BiosAdvisor\Database\BiosDatabase.cs'))
        $match = [regex]::Match($source, 'PublicKeyBlob\s*=\s*"(?<key>[A-Za-z0-9+/=]+)"')
        if (-not $match.Success) { throw 'Chave pública não encontrada em BiosDatabase.PublicKeyBlob.' }
        $rsa.ImportCspBlob([Convert]::FromBase64String($match.Groups['key'].Value))
        $ok = $rsa.VerifyData($bytes, $sha, [Convert]::FromBase64String((Get-Content -LiteralPath $sigPath -Raw).Trim()))
        if ($ok) { Write-Host "Assinatura válida (versão $($json.version))." } else { throw 'Assinatura INVÁLIDA.' }
    } finally { $rsa.Dispose() }
    return
}

if (-not (Test-Path -LiteralPath $privateKeyPath)) { throw "Chave privada não encontrada em $privateKeyPath (crie com -NewKey)." }
$rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.ImportCspBlob([System.IO.File]::ReadAllBytes($privateKeyPath))
    $signature = [Convert]::ToBase64String($rsa.SignData($bytes, $sha))
    Set-Content -LiteralPath $sigPath -Value $signature -NoNewline -Encoding ascii
    Write-Host "Banco v$($json.version) assinado: $sigPath"
} finally { $rsa.Dispose() }
