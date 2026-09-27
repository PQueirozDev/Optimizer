param(
    [Parameter(Mandatory)] [string] $Licensee,
    [Parameter(Mandatory)] [string] $MachineId,
    [datetime] $ExpiresAtUtc
)

$privateKeyPath = Join-Path $PSScriptRoot '..\private\optimizer-license-rsa-private.blob'
if (-not (Test-Path -LiteralPath $privateKeyPath)) {
    throw "Chave privada do emissor não encontrada em: $privateKeyPath"
}

$payload = [ordered]@{
    Product = 'PQueirozOptimizer'
    Licensee = $Licensee.Trim()
    MachineId = ($MachineId.ToUpperInvariant() -replace '[^0-9A-F]', '')
    ExpiresAtUtc = if ($PSBoundParameters.ContainsKey('ExpiresAtUtc')) { $ExpiresAtUtc.ToUniversalTime().ToString('o') } else { $null }
} | ConvertTo-Json -Compress

function To-Base64Url([byte[]] $Bytes) {
    [Convert]::ToBase64String($Bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')
}

$data = [Text.Encoding]::UTF8.GetBytes($payload)
$rsa = [Security.Cryptography.RSACryptoServiceProvider]::new()
try {
    $rsa.ImportCspBlob([IO.File]::ReadAllBytes($privateKeyPath))
    $signature = $rsa.SignData($data, [Security.Cryptography.CryptoConfig]::MapNameToOID('SHA256'))
    "PQO1-$(To-Base64Url $data).$(To-Base64Url $signature)"
}
finally {
    $rsa.PersistKeyInCsp = $false
    $rsa.Dispose()
}
