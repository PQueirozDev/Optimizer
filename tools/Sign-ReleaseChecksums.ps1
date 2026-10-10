<#
.SYNOPSIS
  Signs the release SHA256SUMS.txt so the app can verify that an update was published by the project owner.

.DESCRIPTION
  The app only installs an update automatically when SHA256SUMS.txt.sig is a valid RSA-SHA256 signature of the
  exact bytes of SHA256SUMS.txt, made with the private key matching UpdateService.UpdatePublicKeyBlob.
  The SHA-256 hash proves the installer was not corrupted; the signature proves who published the hash.

  -NewKey   creates the key pair once in private\ (outside git). Back up the private key safely and store it,
            base64-encoded, as the GitHub Actions secret UPDATE_SIGNING_KEY.
  -File     the SHA256SUMS.txt to sign (default: artifacts\installer\SHA256SUMS.txt).
  -KeyPath  private key blob (default: private\update-rsa-private.blob). CI writes the secret to a temp file.
  -Verify   checks the .sig with the public key embedded in the app source.
#>
param(
    [switch]$NewKey,
    [switch]$Verify,
    [string]$File,
    [string]$KeyPath
)

$ErrorActionPreference = 'Stop'
$root = Split-Path $PSScriptRoot -Parent
if (-not $KeyPath) { $KeyPath = Join-Path $root 'private\update-rsa-private.blob' }
$publicPath = Join-Path $root 'private\update-rsa-public.txt'
if (-not $File) { $File = Join-Path $root 'artifacts\installer\SHA256SUMS.txt' }
$sha = [System.Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256')

if ($NewKey) {
    if (Test-Path -LiteralPath $KeyPath) { throw "A key already exists at $KeyPath. Delete it manually only if you really want to replace it (published apps would stop accepting updates)." }
    New-Item -ItemType Directory -Force (Split-Path $KeyPath) | Out-Null
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new(3072)
    try {
        [System.IO.File]::WriteAllBytes($KeyPath, $rsa.ExportCspBlob($true))
        $public = [Convert]::ToBase64String($rsa.ExportCspBlob($false))
        Set-Content -LiteralPath $publicPath -Value $public -NoNewline
        Write-Host "Key pair created."
        Write-Host "Public key (UpdateService.UpdatePublicKeyBlob):"
        Write-Host $public
        Write-Host ""
        Write-Host "GitHub secret UPDATE_SIGNING_KEY = base64 of $KeyPath"
    } finally { $rsa.Dispose() }
    return
}

if (-not (Test-Path -LiteralPath $File)) { throw "File not found: $File" }
$bytes = [System.IO.File]::ReadAllBytes($File)
$sigPath = "$File.sig"

if ($Verify) {
    $source = [System.IO.File]::ReadAllText((Join-Path $root 'PQueirozOptimizer\Services\UpdateService.cs'))
    $match = [regex]::Match($source, 'UpdatePublicKeyBlob\s*=\s*"(?<key>[A-Za-z0-9+/=]+)"')
    if (-not $match.Success) { throw 'UpdatePublicKeyBlob not found in UpdateService.cs.' }
    $rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
    try {
        $rsa.ImportCspBlob([Convert]::FromBase64String($match.Groups['key'].Value))
        $ok = $rsa.VerifyData($bytes, $sha, [Convert]::FromBase64String((Get-Content -LiteralPath $sigPath -Raw).Trim()))
        if ($ok) { Write-Host "Valid signature: $sigPath" } else { throw 'INVALID signature.' }
    } finally { $rsa.Dispose() }
    return
}

if (-not (Test-Path -LiteralPath $KeyPath)) { throw "Private key not found at $KeyPath (create it with -NewKey)." }
$rsa = [System.Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.ImportCspBlob([System.IO.File]::ReadAllBytes($KeyPath))
    if ($rsa.PublicOnly) { throw 'The key file does not contain a private key.' }
    $signature = [Convert]::ToBase64String($rsa.SignData($bytes, $sha))
    Set-Content -LiteralPath $sigPath -Value $signature -NoNewline -Encoding ascii
    Write-Host "Signed: $sigPath"
} finally { $rsa.Dispose() }
